namespace GuiaPlay.Core;

public sealed class LicensingManager : IDisposable
{
    private readonly LicensingApiClient _apiClient;
    private readonly LicensingStateStore _stateStore;
    private readonly WindowsDeviceIdentityStore _deviceIdentityStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PersistedLicensingState _state;
    private readonly LicensingDeviceContext _device;

    public LicensingManager(
        string dataDirectory,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var fullDirectory = Path.GetFullPath(dataDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _apiClient = new LicensingApiClient(httpClient);
        _stateStore = new LicensingStateStore(Path.Combine(fullDirectory, "licensing.dat"));
        _deviceIdentityStore = new WindowsDeviceIdentityStore(Path.Combine(fullDirectory, "device.id"));
        _state = _stateStore.Load();
        _device = new LicensingDeviceContext(
            _deviceIdentityStore.GetOrCreate(),
            Environment.MachineName,
            "Windows",
            ProductInfo.Version);
    }

    public LicensingSnapshot Current { get; private set; } = LicensingSnapshot.Checking;
    public string DeviceId => _device.DeviceId;
    public bool TrialAvailable =>
        _state.ProjectConfig?.Trial.Enabled == true &&
        !_state.TrialConsumed;
    public int TrialOfferDays => Math.Max(0, _state.ProjectConfig?.Trial.Days ?? 0);

    public event EventHandler? StateChanged;

    public async Task<LicensingSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await EvaluateAsync(forceOnlineValidation: false, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LicensingSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await EvaluateAsync(forceOnlineValidation: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task<LicensingSnapshot> StartTrialAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ProjectLicensingConfiguration configuration;
            try
            {
                configuration = await _apiClient.GetProjectConfigAsync(cancellationToken);
                ValidateConfiguration(configuration);
                _state = _state with { ProjectConfig = configuration };
                _ = _stateStore.Save(_state);
            }
            catch (LicensingApiException exception)
            {
                if (exception.ErrorCode is "trial_expired" or "trial_converted")
                {
                    _state = _state with { TrialConsumed = true };
                    _ = _stateStore.Save(_state);
                }

                return PublishOperationError(
                    exception.ErrorCode,
                    exception.Message,
                    LicensingAccessKind.Trial,
                    null);
            }
            catch (InvalidDataException exception)
            {
                return PublishOperationError(
                    "invalid_response",
                    exception.Message,
                    LicensingAccessKind.Trial,
                    null);
            }

            if (!configuration.Trial.Enabled || configuration.Trial.Days <= 0)
            {
                return PublishOperationError(
                    "trial_unavailable",
                    LicensingErrorMessages.For("trial_unavailable"),
                    LicensingAccessKind.Trial,
                    null);
            }

            try
            {
                var view = await _apiClient.StartTrialAsync(
                    _device,
                    NewRequestId(),
                    cancellationToken);
                return AcceptOnline(configuration, view, null);
            }
            catch (LicensingApiException exception)
            {
                return PublishOperationError(
                    exception.ErrorCode,
                    exception.Message,
                    LicensingAccessKind.Trial,
                    null);
            }
            catch (InvalidDataException exception)
            {
                return PublishOperationError(
                    "invalid_response",
                    exception.Message,
                    LicensingAccessKind.Trial,
                    null);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LicensingSnapshot> ActivateAsync(
        string licenseKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeLicenseKey(licenseKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return Publish(Denied(
                LicensingAccessKind.License,
                "invalid_request",
                "Informe uma chave de licença.",
                null));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ProjectLicensingConfiguration configuration;
            try
            {
                configuration = await _apiClient.GetProjectConfigAsync(cancellationToken);
                ValidateConfiguration(configuration);
            }
            catch (LicensingApiException exception)
            {
                return PublishOperationError(
                    exception.ErrorCode,
                    exception.Message,
                    LicensingAccessKind.License,
                    normalizedKey);
            }
            catch (InvalidDataException)
            {
                return PublishOperationError(
                    "invalid_response",
                    LicensingErrorMessages.For("entitlement_invalid"),
                    LicensingAccessKind.License,
                    normalizedKey);
            }

            try
            {
                var view = await _apiClient.ActivateLicenseAsync(
                    _device,
                    normalizedKey,
                    NewRequestId(),
                    cancellationToken);
                return AcceptOnline(configuration, view, normalizedKey);
            }
            catch (LicensingApiException exception)
            {
                return PublishOperationError(
                    exception.ErrorCode,
                    exception.Message,
                    LicensingAccessKind.License,
                    normalizedKey);
            }
            catch (InvalidDataException)
            {
                return PublishOperationError(
                    "invalid_response",
                    "O servidor retornou uma autorização de licença inválida.",
                    LicensingAccessKind.License,
                    normalizedKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LicensingSnapshot> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var key = _state.LicenseKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                return Publish(LicensingSnapshot.Unlicensed(
                    null,
                    "Nenhuma licença paga está ativada neste dispositivo."));
            }

            try
            {
                await _apiClient.DeactivateLicenseAsync(
                    _device,
                    key,
                    NewRequestId(),
                    cancellationToken);
            }
            catch (LicensingApiException exception)
            {
                return PublishOperationError(
                    exception.ErrorCode,
                    exception.Message,
                    LicensingAccessKind.License,
                    key);
            }

            _state = _state with
            {
                LicenseKey = null,
                AccessKind = LicensingAccessKind.None,
                EntitlementToken = null,
                ServerView = null,
                LastOnlineValidationUtc = null,
                LastObservedUtc = MaxUtc(_state.LastObservedUtc, _timeProvider.GetUtcNow()),
                TrialConsumed = true
            };
            _ = _stateStore.Save(_state);
            return Publish(LicensingSnapshot.Unlicensed(
                null,
                "Licença desativada neste dispositivo. A vaga foi liberada."));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LicensingSnapshot> EvaluateAsync(
        bool forceOnlineValidation,
        CancellationToken cancellationToken)
    {
        var configuration = _state.ProjectConfig;
        try
        {
            var onlineConfiguration = await _apiClient.GetProjectConfigAsync(cancellationToken);
            ValidateConfiguration(onlineConfiguration);
            configuration = onlineConfiguration;
            _state = _state with { ProjectConfig = onlineConfiguration };
            _ = _stateStore.Save(_state);
        }
        catch (LicensingApiException exception) when (CanUseCachedAuthorization(exception.ErrorCode))
        {
            // A configuração em cache pode sustentar uma autorização offline já assinada.
        }
        catch (LicensingApiException exception)
        {
            ClearAuthoritativeAuthorization();
            return Publish(Denied(
                _state.AccessKind,
                exception.ErrorCode,
                LicensingErrorMessages.For(exception.ErrorCode),
                _state.LicenseKey));
        }
        catch (InvalidDataException)
        {
            ClearAuthoritativeAuthorization();
            return Publish(Denied(
                _state.AccessKind,
                "invalid_response",
                "A configuração do serviço de licenças é incompatível com o GuiaPlay.",
                _state.LicenseKey));
        }

        if (configuration is null)
        {
            return Publish(Denied(
                _state.AccessKind,
                "network_error",
                "É necessário conectar-se à internet para validar o GuiaPlay pela primeira vez.",
                _state.LicenseKey));
        }

        if (!string.IsNullOrWhiteSpace(_state.LicenseKey))
        {
            return await EvaluateExistingAsync(
                configuration,
                LicensingAccessKind.License,
                forceOnlineValidation,
                cancellationToken);
        }

        if (_state.AccessKind == LicensingAccessKind.Trial &&
            !string.IsNullOrWhiteSpace(_state.EntitlementToken) &&
            _state.ServerView is { AccessKind: LicensingAccessKind.Trial })
        {
            return await EvaluateExistingAsync(
                configuration,
                LicensingAccessKind.Trial,
                forceOnlineValidation,
                cancellationToken);
        }

        var offerMessage = TrialAvailable && configuration.Trial.Days > 0
            ? $"Escolha entre iniciar a avaliação gratuita de {configuration.Trial.Days} dia(s) ou ativar uma licença."
            : "Informe uma chave de licença para ativar o GuiaPlay.";
        return Publish(LicensingSnapshot.Unlicensed(null, offerMessage));
    }

    private async Task<LicensingSnapshot> EvaluateExistingAsync(
        ProjectLicensingConfiguration configuration,
        LicensingAccessKind accessKind,
        bool forceOnlineValidation,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var localVerification = VerifyLocal(configuration, accessKind, now);
        var validationHours = accessKind == LicensingAccessKind.License
            ? configuration.License.ValidationHours
            : configuration.Trial.ValidationHours;
        var lastOnline = _state.LastOnlineValidationUtc;
        var due = lastOnline is null;
        if (lastOnline is { } validatedAt)
        {
            var elapsed = now - validatedAt.ToUniversalTime();
            due = elapsed < TimeSpan.Zero || elapsed >= TimeSpan.FromHours(validationHours);
        }

        if (!forceOnlineValidation && !due && localVerification.IsValid)
        {
            RememberObservedTime(now);
            return Publish(BuildSnapshot(
                _state.ServerView!,
                _state.LicenseKey,
                usingOfflineAuthorization: false,
                message: null));
        }

        try
        {
            LicensingServerView onlineView;
            if (accessKind == LicensingAccessKind.License)
            {
                onlineView = await _apiClient.ValidateLicenseAsync(
                    _device,
                    _state.LicenseKey!,
                    NewRequestId(),
                    cancellationToken);
            }
            else
            {
                onlineView = await _apiClient.ValidateTrialAsync(
                    _device,
                    NewRequestId(),
                    cancellationToken);
            }

            return AcceptOnline(configuration, onlineView, _state.LicenseKey);
        }
        catch (LicensingApiException exception) when (CanUseCachedAuthorization(exception.ErrorCode))
        {
            if (localVerification.IsValid)
            {
                RememberObservedTime(now);
                var message = exception.ErrorCode == "rate_limited"
                    ? "Validação online temporariamente limitada; a autorização offline assinada continua válida."
                    : "Sem conexão; usando a autorização offline assinada.";
                return Publish(BuildSnapshot(
                    _state.ServerView!,
                    _state.LicenseKey,
                    usingOfflineAuthorization: true,
                    message));
            }

            var localError = localVerification.ErrorCode ?? exception.ErrorCode;
            return Publish(Denied(
                accessKind,
                localError,
                LicensingErrorMessages.For(localError),
                _state.LicenseKey));
        }
        catch (LicensingApiException exception)
        {
            ClearAuthoritativeAuthorization(keepLicenseKey: accessKind == LicensingAccessKind.License);
            return Publish(Denied(
                accessKind,
                exception.ErrorCode,
                LicensingErrorMessages.For(exception.ErrorCode),
                _state.LicenseKey));
        }
        catch (InvalidDataException)
        {
            ClearAuthoritativeAuthorization(keepLicenseKey: accessKind == LicensingAccessKind.License);
            return Publish(Denied(
                accessKind,
                "invalid_response",
                "O servidor retornou uma autorização incompatível com o GuiaPlay.",
                _state.LicenseKey));
        }
    }

    private LicensingSnapshot AcceptOnline(
        ProjectLicensingConfiguration configuration,
        LicensingServerView view,
        string? licenseKey)
    {
        if (view.AccessKind == LicensingAccessKind.None ||
            !string.Equals(view.Status, "active", StringComparison.Ordinal))
        {
            return Publish(Denied(
                view.AccessKind,
                view.Status,
                LicensingErrorMessages.For(view.Status),
                licenseKey));
        }

        var now = _timeProvider.GetUtcNow();
        var verification = EntitlementVerifier.Verify(
            view.EntitlementToken,
            configuration.Signing,
            view.AccessKind,
            _device.DeviceId,
            now,
            null,
            enforceLocalTimeWindow: false);
        if (!verification.IsValid ||
            verification.ExpiresAt != view.ExpiresAt ||
            verification.OfflineUntil != view.OfflineUntil)
        {
            return Publish(Denied(
                view.AccessKind,
                verification.ErrorCode ?? "entitlement_invalid",
                LicensingErrorMessages.For(verification.ErrorCode ?? "entitlement_invalid"),
                licenseKey));
        }

        var trialConsumed =
            _state.TrialConsumed ||
            view.AccessKind is LicensingAccessKind.Trial or LicensingAccessKind.License;

        _state = new PersistedLicensingState(
            PersistedLicensingState.CurrentSchemaVersion,
            view.AccessKind == LicensingAccessKind.License ? licenseKey : null,
            view.AccessKind,
            view.EntitlementToken,
            view,
            configuration,
            view.ServerTime,
            now.ToUniversalTime(),
            trialConsumed);

        var saved = _stateStore.Save(_state);
        return Publish(BuildSnapshot(
            view,
            _state.LicenseKey,
            usingOfflineAuthorization: false,
            saved ? null : "Licença válida, mas o estado local protegido não pôde ser salvo."));
    }

    private EntitlementVerificationResult VerifyLocal(
        ProjectLicensingConfiguration configuration,
        LicensingAccessKind accessKind,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_state.EntitlementToken) ||
            _state.ServerView is not { } view ||
            view.AccessKind != accessKind)
        {
            return new EntitlementVerificationResult(
                false,
                "entitlement_invalid",
                null,
                null,
                null,
                null);
        }

        var result = EntitlementVerifier.Verify(
            _state.EntitlementToken,
            configuration.Signing,
            accessKind,
            _device.DeviceId,
            now,
            _state.LastObservedUtc);
        if (!result.IsValid)
        {
            return result;
        }

        if (result.ExpiresAt != view.ExpiresAt ||
            result.OfflineUntil != view.OfflineUntil)
        {
            return result with { IsValid = false, ErrorCode = "entitlement_invalid" };
        }

        return result;
    }

    private void RememberObservedTime(DateTimeOffset now)
    {
        _state = _state with
        {
            LastObservedUtc = MaxUtc(_state.LastObservedUtc, now)
        };
        _ = _stateStore.Save(_state);
    }

    private void ClearAuthoritativeAuthorization(bool keepLicenseKey = true)
    {
        _state = _state with
        {
            LicenseKey = keepLicenseKey ? _state.LicenseKey : null,
            EntitlementToken = null,
            ServerView = null,
            LastOnlineValidationUtc = null,
            LastObservedUtc = MaxUtc(_state.LastObservedUtc, _timeProvider.GetUtcNow())
        };
        _ = _stateStore.Save(_state);
    }

    private LicensingSnapshot BuildSnapshot(
        LicensingServerView view,
        string? licenseKey,
        bool usingOfflineAuthorization,
        string? message)
    {
        return new LicensingSnapshot(
            true,
            view.AccessKind,
            view.Status,
            null,
            message,
            licenseKey,
            view.CustomerName,
            view.CustomerEmail,
            view.PlanName,
            view.StartedAt,
            view.ActivatedAt,
            view.ExpiresAt,
            view.Lifetime,
            view.AccessKind == LicensingAccessKind.License ? view.MaxDevices : null,
            view.AccessKind == LicensingAccessKind.Trial ? view.DurationDays : null,
            _state.LastOnlineValidationUtc,
            view.OfflineUntil,
            usingOfflineAuthorization);
    }

    private LicensingSnapshot Denied(
        LicensingAccessKind accessKind,
        string errorCode,
        string message,
        string? licenseKey)
    {
        var view = _state.ServerView;
        return new LicensingSnapshot(
            false,
            accessKind,
            errorCode,
            errorCode,
            message,
            licenseKey,
            view?.CustomerName,
            view?.CustomerEmail,
            view?.PlanName,
            view?.StartedAt,
            view?.ActivatedAt,
            view?.ExpiresAt,
            view?.Lifetime ?? false,
            view?.MaxDevices,
            view?.DurationDays,
            _state.LastOnlineValidationUtc,
            view?.OfflineUntil,
            false);
    }

    private LicensingSnapshot PublishOperationError(
        string errorCode,
        string message,
        LicensingAccessKind requestedKind,
        string? requestedLicenseKey)
    {
        if (Current.HasAccess)
        {
            return Publish(Current with
            {
                ErrorCode = errorCode,
                Message = message
            });
        }

        return Publish(Denied(requestedKind, errorCode, message, requestedLicenseKey));
    }

    private LicensingSnapshot Publish(LicensingSnapshot snapshot)
    {
        Current = snapshot;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }

    private static bool CanUseCachedAuthorization(string errorCode) =>
        errorCode is
            "network_error" or
            "rate_limited" or
            "internal_error" or
            "upstream_error" or
            "upstream_timeout" or
            "signing_unavailable";

    private static void ValidateConfiguration(ProjectLicensingConfiguration configuration)
    {
        if (!string.Equals(configuration.ProtocolVersion, LicensingProtocol.Version, StringComparison.Ordinal) ||
            !string.Equals(configuration.ProjectId, LicensingProtocol.ProjectId, StringComparison.Ordinal) ||
            !string.Equals(configuration.IntegrationCode, LicensingProtocol.IntegrationCode, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Configuração GSL-v1 incompatível com o GuiaPlay.");
        }
    }

    private static string NormalizeLicenseKey(string value) =>
        value.Trim().ToUpperInvariant();

    private static string NewRequestId() => Guid.NewGuid().ToString();

    private static DateTimeOffset? MaxUtc(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right?.ToUniversalTime();
        }

        if (right is null)
        {
            return left.Value.ToUniversalTime();
        }

        var leftUtc = left.Value.ToUniversalTime();
        var rightUtc = right.Value.ToUniversalTime();
        return leftUtc >= rightUtc ? leftUtc : rightUtc;
    }

    public void Dispose()
    {
        _apiClient.Dispose();
        _gate.Dispose();
    }
}
