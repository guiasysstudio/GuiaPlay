; GuiaPlay WebView2 Evergreen runtime prerequisite for YouTube (Inno Setup 6.x).
; Keeps the stable LocalAppData-per-user installer and never replaces LibVLC.
[Code]
function GuiaPlayHasWebView2Runtime: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := False;
  if RegQueryStringValue(HKCU, Key, 'pv', Version) and
      (Version <> '') and (Version <> '0.0.0.0') then
    Result := True;
  if not Result and RegQueryStringValue(HKLM32, Key, 'pv', Version) and
      (Version <> '') and (Version <> '0.0.0.0') then
    Result := True;
  if not Result and IsWin64 then
    if RegQueryStringValue(HKLM64, Key, 'pv', Version) and
        (Version <> '') and (Version <> '0.0.0.0') then
      Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  SetupFile: String;
begin
  if (CurStep <> ssPostInstall) or GuiaPlayHasWebView2Runtime then
    Exit;

  ; Prevents a successful GuiaPlay installation from being reported as failed
  ; simply because Microsoft download servers cannot be reached.
  try
    Log('WebView2 runtime ausente; instalando bootstrapper Evergreen oficial da Microsoft.');
    DownloadTemporaryFile(
      'https://go.microsoft.com/fwlink/p/?LinkId=2124703',
      'GuiaPlay-WebView2-Setup.exe', '', nil);
    SetupFile := ExpandConstant('{tmp}\GuiaPlay-WebView2-Setup.exe');
    ExitCode := -1;
    if not Exec(SetupFile, '/silent /install', '', SW_HIDE,
        ewWaitUntilTerminated, ExitCode) then
      Log('Não foi possível executar o instalador WebView2.')
    else
      Log('Instalador WebView2 concluiu com código: ' + IntToStr(ExitCode));
  except
    Log('Não foi possível baixar/instalar WebView2: ' + GetExceptionMessage);
  end;

  if not GuiaPlayHasWebView2Runtime then
    MsgBox(
      'O GuiaPlay foi instalado, mas o componente Microsoft Edge WebView2 Runtime ' +
      'não pôde ser instalado automaticamente.' + #13#10 + #13#10 +
      'Para reproduzir vídeos do YouTube no GuiaPlay, instale o runtime oficial da Microsoft:' +
      #13#10 + 'https://developer.microsoft.com/microsoft-edge/webview2',
      mbInformation, MB_OK);
end;
