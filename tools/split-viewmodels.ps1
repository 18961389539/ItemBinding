# One-off split script. ASCII-only (Windows PowerShell 5.1 + GBK codepage safety).
# Splits HomeViewModel.cs and RecipeViewModel.cs into partial class files.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # script lives in <repo>/tools
$homeFile = Join-Path $root 'MainAPP\ViewModels\HomeViewModel.cs'
$recipeFile = Join-Path $root 'MainAPP\ViewModels\RecipeViewModel.cs'

function Out-FileUtf8([string]$path, [string]$content, [bool]$bom) {
    $enc = New-Object System.Text.UTF8Encoding($bom)
    [System.IO.File]::WriteAllText($path, $content, $enc)
}

# Slices [start1..end1, start2..end2, ...] (1-based inclusive) from $src.
# NOTE: do NOT pass nested arrays (PowerShell flattens them); pass parallel int arrays.
function Slice([string[]]$src, [int[]]$begins, [int[]]$ends) {
    $out = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $begins.Count; $i++) {
        $s = $begins[$i]; $e = $ends[$i]
        $out.AddRange([string[]]$src[($s - 1)..($e - 1)])
    }
    return ,($out.ToArray())
}

function HasBom([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    return $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
}

function Build-Partial([string]$path, [string]$className, [string[]]$usings, [string[]]$body, [bool]$bom, [string]$note) {
    $header = @(
        '// ============================================================'
        "// Split from the original monolithic file: $note"
        '// Reason: original file too large (>90KB); split by single responsibility into partial class files'
        '// to improve navigability and review locality. See docs/knowledge for the file index.'
        '// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern'
        '#pragma warning disable VSTHRD001'
        ''
        'namespace MainAPP.ViewModels'
        '{'
        "    public partial class $className"
        '    {'
    )
    $footer = @('    }','}')
    $all = New-Object System.Collections.Generic.List[string]
    $all.AddRange([string[]]$usings)
    $all.AddRange([string[]]$header)
    $all.AddRange([string[]]$body)
    $all.AddRange([string[]]$footer)
    Out-FileUtf8 $path (($all.ToArray()) -join "`r`n") $bom
    Write-Host "written: $path"
}

# ================= HomeViewModel =================
$homeLines = [System.IO.File]::ReadAllLines($homeFile)  # 0-based
$homeBom = HasBom $homeFile
$hUsings = $homeLines[0..32]  # lines 1-33

$hDisplay   = Slice $homeLines @(541)          @(822)
$hMainLoop  = Slice $homeLines @(828,969,1072,1141,1789) @(968,1071,1140,1238,1797)
$hProcess   = Slice $homeLines @(1239)         @(1453)
$hDraw      = Slice $homeLines @(115,172,1454) @(133,191,1785)
$hReadImage = Slice $homeLines @(1786,1798,1838) @(1788,1837,2136)

$pointer = @(
    ''
    '// ------------------------------------------------------------------'
    '// This class is split into partial files by responsibility (see docs/knowledge for the file index):'
    '//   HomeViewModel.Display.cs     - ImageForShow cache and status-bar metrics (drop rate / bind delay / encoder link / scanner status)'
    '//   HomeViewModel.MainLoop.cs    - main loop, decode, inference scheduling, drain wait, image channel'
    '//   HomeViewModel.Process.cs     - per-frame pipeline ProcessImageAsync (inference -> bind -> persist -> send entry)'
    '//   HomeViewModel.Draw.cs        - boxes / barcode / direction-arrow drawing and inference-results refresh'
    '//   HomeViewModel.ReadImage.cs   - frame grab loop, packet-loss detection, readiness checks, calibration helpers'
    '// Put new members in the matching file; keep this file for fields and lifecycle only.'
    '// ------------------------------------------------------------------'
    ''
)
$homeOut = New-Object System.Collections.Generic.List[string]
$homeOut.AddRange([string[]]$homeLines[0..113])   # 1-114
$homeOut.AddRange([string[]]$homeLines[133..170]) # 134-171
$homeOut.AddRange([string[]]$homeLines[191..539]) # 192-540
foreach ($c in $pointer) { $homeOut.Add($c) }
$homeOut.AddRange([string[]]$homeLines[822..826]) # 823-827
$homeOut.AddRange([string[]]$homeLines[2136..2223]) # 2137-2224
Out-FileUtf8 $homeFile (($homeOut.ToArray()) -join "`r`n") $homeBom
Write-Host "rebuilt: $homeFile"

Build-Partial (Join-Path $root 'MainAPP\ViewModels\HomeViewModel.Display.cs')   'HomeViewModel' $hUsings $hDisplay   $homeBom 'HomeViewModel display + frame metrics'
Build-Partial (Join-Path $root 'MainAPP\ViewModels\HomeViewModel.MainLoop.cs')  'HomeViewModel' $hUsings $hMainLoop  $homeBom 'HomeViewModel main loop + inference scheduling'
Build-Partial (Join-Path $root 'MainAPP\ViewModels\HomeViewModel.Process.cs')   'HomeViewModel' $hUsings $hProcess   $homeBom 'HomeViewModel per-frame processing pipeline'
Build-Partial (Join-Path $root 'MainAPP\ViewModels\HomeViewModel.Draw.cs')      'HomeViewModel' $hUsings $hDraw      $homeBom 'HomeViewModel image drawing'
Build-Partial (Join-Path $root 'MainAPP\ViewModels\HomeViewModel.ReadImage.cs') 'HomeViewModel' $hUsings $hReadImage $homeBom 'HomeViewModel frame grab + readiness'

# ================= RecipeViewModel =================
# Main keeps: template-info region (1-670) + command region start (672-708) +
#             SaveSilently/BrowseImageDirectory (1238-1265) + command #endregion (1568-1569) +
#             blank (1765) + Dispose (1910-1944)
# Partials split by the file's own #region method groups:
#   Capture   = device params + get image + save displayed image (1266-1567)
#   Inference = test results + inference (709-1237)
#   Preview   = live display + debug info incl reference points (1570-1764)
#   Optimize  = parameter optimization (1766-1909)
$recipeLines = [System.IO.File]::ReadAllLines($recipeFile)
$recipeBom = HasBom $recipeFile
$rUsings = $recipeLines[0..22]  # lines 1-23

$rCapture   = Slice $recipeLines @(1266) @(1567)
$rInference = Slice $recipeLines @(709)  @(1237)
$rPreview   = Slice $recipeLines @(1570) @(1764)
$rOptimize  = Slice $recipeLines @(1766) @(1909)

$recipeOut = New-Object System.Collections.Generic.List[string]
$recipeOut.AddRange([string[]]$recipeLines[0..669])     # 1-670
$recipeOut.AddRange([string[]]$recipeLines[671..707])   # 672-708
$recipeOut.AddRange([string[]]$recipeLines[1237..1264]) # 1238-1265
$recipeOut.AddRange([string[]]$recipeLines[1567..1568]) # 1568-1569
$recipeOut.AddRange([string[]]$recipeLines[1764..1764]) # 1765
$recipeOut.AddRange([string[]]$recipeLines[1909..1943]) # 1910-1944
Out-FileUtf8 $recipeFile (($recipeOut.ToArray()) -join "`r`n") $recipeBom
Write-Host "rebuilt: $recipeFile"

Build-Partial (Join-Path $root 'MainAPP\ViewModels\RecipeViewModel.Capture.cs')   'RecipeViewModel' $rUsings $rCapture   $recipeBom 'RecipeViewModel image capture + device parameters'
Build-Partial (Join-Path $root 'MainAPP\ViewModels\RecipeViewModel.Inference.cs') 'RecipeViewModel' $rUsings $rInference $recipeBom 'RecipeViewModel test inference'
Build-Partial (Join-Path $root 'MainAPP\ViewModels\RecipeViewModel.Preview.cs')   'RecipeViewModel' $rUsings $rPreview   $recipeBom 'RecipeViewModel live display + calibration debug'
Build-Partial (Join-Path $root 'MainAPP\ViewModels\RecipeViewModel.Optimize.cs')  'RecipeViewModel' $rUsings $rOptimize  $recipeBom 'RecipeViewModel parameter optimization'

Write-Host 'ALL DONE'