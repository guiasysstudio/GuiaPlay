[CmdletBinding()]
param(
    [string]$EdgePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logoSource = Join-Path $repoRoot 'src\GuiaPlay.App\Assets\Branding\Source\GuiaPlay-Logo.svg'
$wordmarkSource = Join-Path $repoRoot 'src\GuiaPlay.App\Assets\Branding\Source\GuiaPlay-Wordmark.svg'

foreach ($source in @($logoSource, $wordmarkSource)) {
    if (-not (Test-Path -LiteralPath $source) -or (Get-Item -LiteralPath $source).Length -eq 0) {
        throw "SVG oficial ausente ou vazio: $source"
    }
}

if ([string]::IsNullOrWhiteSpace($EdgePath)) {
    $edgeCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $EdgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($EdgePath) -or -not (Test-Path -LiteralPath $EdgePath)) {
    throw 'Microsoft Edge não foi encontrado. Informe -EdgePath para renderizar os SVGs oficiais.'
}

Add-Type -AssemblyName System.Drawing
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("GuiaPlayBranding-" + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($stageRoot) | Out-Null

function Assert-ImageSize {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    if (-not (Test-Path -LiteralPath $Path) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Imagem não foi gerada: $Path"
    }

    $image = [System.Drawing.Image]::FromFile($Path)
    try {
        if ($image.Width -ne $Width -or $image.Height -ne $Height) {
            throw "Dimensões inesperadas em ${Path}: $($image.Width)x$($image.Height), esperado ${Width}x${Height}."
        }
    }
    finally {
        $image.Dispose()
    }
}

function Assert-ImageContent {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [bool]$Transparent
    )

    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        $stepX = [Math]::Max(1, [int]($bitmap.Width / 128))
        $stepY = [Math]::Max(1, [int]($bitmap.Height / 128))
        $background = $bitmap.GetPixel(0, 0)
        $minX = $bitmap.Width
        $minY = $bitmap.Height
        $maxX = -1
        $maxY = -1
        for ($y = 0; $y -lt $bitmap.Height; $y += $stepY) {
            for ($x = 0; $x -lt $bitmap.Width; $x += $stepX) {
                $pixel = $bitmap.GetPixel($x, $y)
                $hasContent = if ($Transparent) {
                    $pixel.A -gt 16
                }
                else {
                    ([Math]::Abs([int]$pixel.R - [int]$background.R) +
                        [Math]::Abs([int]$pixel.G - [int]$background.G) +
                        [Math]::Abs([int]$pixel.B - [int]$background.B)) -gt 24
                }
                if ($hasContent) {
                    $minX = [Math]::Min($minX, $x)
                    $minY = [Math]::Min($minY, $y)
                    $maxX = [Math]::Max($maxX, $x)
                    $maxY = [Math]::Max($maxY, $y)
                }
            }
        }

        $contentWidth = $maxX - $minX + 1
        $contentHeight = $maxY - $minY + 1
        if ($contentWidth -lt ($bitmap.Width * 0.6) -or $contentHeight -lt ($bitmap.Height * 0.45)) {
            throw "Render incompleto ou vazio em ${Path}: conteúdo ${contentWidth}x${contentHeight}."
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Invoke-EdgeRender {
    param(
        [Parameter(Mandatory)] [string]$Html,
        [Parameter(Mandatory)] [string]$OutputPath,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height,
        [bool]$Transparent = $true
    )

    $renderId = [Guid]::NewGuid().ToString('N')
    $htmlPath = Join-Path $stageRoot "$renderId.html"
    [System.IO.File]::WriteAllText($htmlPath, $Html, $utf8NoBom)

    $backgroundArgument = if ($Transparent) { '--default-background-color=00000000' } else { '--default-background-color=fff7f9fc' }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $profilePath = Join-Path $stageRoot "profile-$renderId-$attempt"
        [System.IO.Directory]::CreateDirectory($profilePath) | Out-Null
        $arguments = @(
            '--headless=new',
            '--disable-gpu',
            '--hide-scrollbars',
            '--no-first-run',
            '--force-device-scale-factor=1',
            '--run-all-compositor-stages-before-draw',
            '--virtual-time-budget=1000',
            '--disable-features=msEdgeFirstRunExperience',
            $backgroundArgument,
            "--user-data-dir=$profilePath",
            "--window-size=$Width,$Height",
            "--screenshot=$OutputPath",
            ([Uri]::new($htmlPath)).AbsoluteUri
        )

        & $EdgePath @arguments | Out-Null
        if ($LASTEXITCODE -ne 0) {
            if ($attempt -eq 3) {
                throw "Edge falhou ao renderizar $OutputPath (exit code $LASTEXITCODE)."
            }
            continue
        }

        try {
            Assert-ImageSize -Path $OutputPath -Width $Width -Height $Height
            Assert-ImageContent -Path $OutputPath -Transparent $Transparent
            return
        }
        catch {
            if ($attempt -eq 3) {
                throw
            }
        }
    }
}

function New-SvgPng {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$OutputPath,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    $sourceUri = ([Uri]::new($Source)).AbsoluteUri
    $html = @"
<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent}
body{display:flex;align-items:center;justify-content:center}
img{display:block;width:100%;height:100%;object-fit:contain}
</style></head><body><img src="$sourceUri" alt=""></body></html>
"@
    Invoke-EdgeRender -Html $html -OutputPath $OutputPath -Width $Width -Height $Height -Transparent $true
}

function Resize-Png {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$OutputPath,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    $source = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $Width, $Height))
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    Assert-ImageSize -Path $OutputPath -Width $Width -Height $Height
    Assert-ImageContent -Path $OutputPath -Transparent $true
}

function New-BrandingComposite {
    param(
        [Parameter(Mandatory)] [string]$OutputPath,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height,
        [Parameter(Mandatory)] [string]$LogoPath,
        [Parameter(Mandatory)] [System.Drawing.Rectangle]$LogoBounds,
        [string]$WordmarkPath,
        [System.Drawing.Rectangle]$WordmarkBounds
    )

    $logo = [System.Drawing.Image]::FromFile($LogoPath)
    $wordmark = if ([string]::IsNullOrWhiteSpace($WordmarkPath)) { $null } else { [System.Drawing.Image]::FromFile($WordmarkPath) }
    try {
        $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::FromArgb(247, 249, 252))
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage($logo, $LogoBounds)
                if ($null -ne $wordmark) {
                    $graphics.DrawImage($wordmark, $WordmarkBounds)
                }
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $logo.Dispose()
        if ($null -ne $wordmark) {
            $wordmark.Dispose()
        }
    }

    Assert-ImageSize -Path $OutputPath -Width $Width -Height $Height
    Assert-ImageContent -Path $OutputPath -Transparent $false
}

function Convert-ToBmp24 {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$OutputPath
    )

    $source = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        $bitmap = [System.Drawing.Bitmap]::new(
            $source.Width,
            $source.Height,
            [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::FromArgb(247, 249, 252))
                $graphics.DrawImageUnscaled($source, 0, 0)
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

function New-PngIcon {
    param(
        [Parameter(Mandatory)] [string[]]$ImagePaths,
        [Parameter(Mandatory)] [string]$OutputPath
    )

    $images = foreach ($imagePath in $ImagePaths) {
        $bytes = [System.IO.File]::ReadAllBytes($imagePath)
        $image = [System.Drawing.Image]::FromFile($imagePath)
        try {
            [pscustomobject]@{ Width = $image.Width; Height = $image.Height; Bytes = $bytes }
        }
        finally {
            $image.Dispose()
        }
    }

    $stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)
        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $widthByte = if ($image.Width -eq 256) { [byte]0 } else { [byte]$image.Width }
            $heightByte = if ($image.Height -eq 256) { [byte]0 } else { [byte]$image.Height }
            $writer.Write($widthByte)
            $writer.Write($heightByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }
        foreach ($image in $images) {
            $writer.Write($image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

try {
    $iconSizes = @(16, 24, 32, 48, 64, 128, 256, 512)
    $logoMasterPath = Join-Path $stageRoot 'GuiaPlay-Symbol-Original.png'
    New-SvgPng -Source $logoSource -OutputPath $logoMasterPath -Width 1240 -Height 1240
    $iconPaths = @{}
    foreach ($size in $iconSizes) {
        $path = Join-Path $stageRoot "GuiaPlay-Icon-$size.png"
        Resize-Png -SourcePath $logoMasterPath -OutputPath $path -Width $size -Height $size
        $iconPaths[$size] = $path
    }

    $wordmarkMasterPath = Join-Path $stageRoot 'GuiaPlay-Wordmark-Original.png'
    New-SvgPng -Source $wordmarkSource -OutputPath $wordmarkMasterPath -Width 2034 -Height 520
    $wordmarkSizes = @(
        @{ Name = 'GuiaPlay-Wordmark-1024.png'; Width = 1024; Height = 262 },
        @{ Name = 'GuiaPlay-Wordmark-768.png'; Width = 768; Height = 196 },
        @{ Name = 'GuiaPlay-Wordmark-512.png'; Width = 512; Height = 131 },
        @{ Name = 'GuiaPlay-Wordmark-UI.png'; Width = 900; Height = 230 }
    )
    foreach ($wordmark in $wordmarkSizes) {
        Resize-Png -SourcePath $wordmarkMasterPath -OutputPath (Join-Path $stageRoot $wordmark.Name) -Width $wordmark.Width -Height $wordmark.Height
    }

    New-BrandingComposite -OutputPath (Join-Path $stageRoot 'GuiaPlay-Banner-700x200.png') -Width 700 -Height 200 `
        -LogoPath $logoMasterPath -LogoBounds ([System.Drawing.Rectangle]::new(34, 29, 142, 142)) `
        -WordmarkPath $wordmarkMasterPath -WordmarkBounds ([System.Drawing.Rectangle]::new(204, 41, 462, 118))
    New-BrandingComposite -OutputPath (Join-Path $stageRoot 'WizardImageFile-preview.png') -Width 164 -Height 314 `
        -LogoPath $logoMasterPath -LogoBounds ([System.Drawing.Rectangle]::new(26, 61, 112, 112)) `
        -WordmarkPath $wordmarkMasterPath -WordmarkBounds ([System.Drawing.Rectangle]::new(12, 211, 140, 36))
    New-BrandingComposite -OutputPath (Join-Path $stageRoot 'WizardSmallImageFile-preview.png') -Width 55 -Height 58 `
        -LogoPath $logoMasterPath -LogoBounds ([System.Drawing.Rectangle]::new(5, 6, 45, 45))

    Convert-ToBmp24 -SourcePath (Join-Path $stageRoot 'WizardImageFile-preview.png') -OutputPath (Join-Path $stageRoot 'WizardImageFile.bmp')
    Convert-ToBmp24 -SourcePath (Join-Path $stageRoot 'WizardSmallImageFile-preview.png') -OutputPath (Join-Path $stageRoot 'WizardSmallImageFile.bmp')

    $icoPath = Join-Path $stageRoot 'GuiaPlay.ico'
    New-PngIcon -ImagePaths @($iconPaths[16], $iconPaths[24], $iconPaths[32], $iconPaths[48], $iconPaths[64], $iconPaths[128], $iconPaths[256]) -OutputPath $icoPath

    $destinations = [ordered]@{
        'src\GuiaPlay.App\Assets\Branding\GuiaPlay-Icon-UI.png' = $iconPaths[256]
        'src\GuiaPlay.App\Assets\Branding\GuiaPlay-Wordmark-UI.png' = (Join-Path $stageRoot 'GuiaPlay-Wordmark-UI.png')
        'src\GuiaPlay.App\Assets\Branding\Icons\GuiaPlay.ico' = $icoPath
        'src\GuiaPlay.App\Assets\Branding\Icons\GuiaPlay-Symbol-Original.png' = $logoMasterPath
        'src\GuiaPlay.App\Assets\Branding\Icons\GuiaPlay-Symbol-128.png' = $iconPaths[128]
        'src\GuiaPlay.App\Assets\Branding\Icons\GuiaPlay-Symbol-256.png' = $iconPaths[256]
        'src\GuiaPlay.App\Assets\Branding\Wordmarks\GuiaPlay-Wordmark-Original.png' = $wordmarkMasterPath
        'src\GuiaPlay.App\Assets\Branding\Wordmarks\GuiaPlay-Wordmark-1024.png' = (Join-Path $stageRoot 'GuiaPlay-Wordmark-1024.png')
        'src\GuiaPlay.App\Assets\Branding\Wordmarks\GuiaPlay-Wordmark-768.png' = (Join-Path $stageRoot 'GuiaPlay-Wordmark-768.png')
        'src\GuiaPlay.App\Assets\Branding\Wordmarks\GuiaPlay-Wordmark-512.png' = (Join-Path $stageRoot 'GuiaPlay-Wordmark-512.png')
        'installer\Assets\GuiaPlay-Setup.ico' = $icoPath
        'installer\Assets\GuiaPlay-Banner-700x200.png' = (Join-Path $stageRoot 'GuiaPlay-Banner-700x200.png')
        'installer\Assets\WizardImageFile-preview.png' = (Join-Path $stageRoot 'WizardImageFile-preview.png')
        'installer\Assets\WizardImageFile.bmp' = (Join-Path $stageRoot 'WizardImageFile.bmp')
        'installer\Assets\WizardSmallImageFile-preview.png' = (Join-Path $stageRoot 'WizardSmallImageFile-preview.png')
        'installer\Assets\WizardSmallImageFile.bmp' = (Join-Path $stageRoot 'WizardSmallImageFile.bmp')
    }
    foreach ($size in $iconSizes) {
        $destinations["src\GuiaPlay.App\Assets\Branding\Icons\GuiaPlay-Icon-$size.png"] = $iconPaths[$size]
    }

    foreach ($destination in $destinations.GetEnumerator()) {
        $destinationPath = Join-Path $repoRoot $destination.Key
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destinationPath)) | Out-Null
        Copy-Item -LiteralPath $destination.Value -Destination $destinationPath -Force
    }

    $manifestAssets = foreach ($destination in $destinations.GetEnumerator()) {
        $destinationPath = Join-Path $repoRoot $destination.Key
        $width = $null
        $height = $null
        try {
            $image = [System.Drawing.Image]::FromFile($destinationPath)
            try {
                $width = $image.Width
                $height = $image.Height
            }
            finally {
                $image.Dispose()
            }
        }
        catch [System.ArgumentException] {
            # Keep dimensions null for a format System.Drawing cannot inspect.
        }

        [ordered]@{
            path = $destination.Key.Replace('\', '/')
            width = $width
            height = $height
            sha256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        schema = 2
        package = 'GuiaPlay official brand assets'
        sourceOfTruth = @(
            [ordered]@{
                path = 'src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Logo.svg'
                sha256 = (Get-FileHash -LiteralPath $logoSource -Algorithm SHA256).Hash.ToLowerInvariant()
            },
            [ordered]@{
                path = 'src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Wordmark.svg'
                sha256 = (Get-FileHash -LiteralPath $wordmarkSource -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        )
        generator = 'scripts/generate-branding-assets.ps1'
        renderer = 'Microsoft Edge (SVG) + System.Drawing (resize, composition, ICO and BMP)'
        assets = @($manifestAssets)
    }
    $manifestPath = Join-Path $repoRoot 'docs\branding\brand-manifest.json'
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $utf8NoBom)

    Write-Host "Branding oficial regenerado a partir dos SVGs em $($destinations.Count) assets."
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}
