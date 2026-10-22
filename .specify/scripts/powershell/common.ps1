#!/usr/bin/env pwsh
# Common PowerShell functions analogous to common.sh

function Get-RepoRoot {
    try {
        $result = git rev-parse --show-toplevel 2>$null
        if ($LASTEXITCODE -eq 0) {
            return $result
        }
    } catch {
        # Git command failed
    }
    
    # Fall back to script location for non-git repos
    return (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
}

function Get-CurrentBranch {
    # First check if SPECIFY_FEATURE environment variable is set
    if ($env:SPECIFY_FEATURE) {
        return $env:SPECIFY_FEATURE
    }
    
    # Then check git if available
    try {
        $result = git rev-parse --abbrev-ref HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            return $result
        }
    } catch {
        # Git command failed
    }
    
    # For non-git repos, try to find the latest feature directory
    $repoRoot = Get-RepoRoot
    $specsDir = Join-Path $repoRoot "specs"
    
    if (Test-Path $specsDir) {
        $latestFeature = ""
        $highest = 0
        
        Get-ChildItem -Path $specsDir -Directory | ForEach-Object {
            if ($_.Name -match '^(\d{3})-') {
                $num = [int]$matches[1]
                if ($num -gt $highest) {
                    $highest = $num
                    $latestFeature = $_.Name
                }
            }
        }
        
        if ($latestFeature) {
            return $latestFeature
        }
    }
    
    # Final fallback
    return "main"
}

function Test-HasGit {
    try {
        git rev-parse --show-toplevel 2>$null | Out-Null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Test-FeatureBranch {
    param(
        [string]$Branch,
        [bool]$HasGit = $true
    )
    
    # For non-git repos, we can't enforce branch naming but still provide output
    if (-not $HasGit) {
        Write-Warning "[specify] Warning: Git repository not detected; skipped branch validation"
        return $true
    }
    
    if ($Branch -notmatch '^[0-9]{3}-') {
        Write-Output "ERROR: Not on a feature branch. Current branch: $Branch"
        Write-Output "Feature branches should be named like: 001-feature-name"
        return $false
    }
    return $true
}

function Get-FeatureDir {
    param([string]$RepoRoot, [string]$Branch)
    Join-Path $RepoRoot "specs/$Branch"
}

function Get-FeaturePathsEnv {
    $repoRoot = Get-RepoRoot
    $currentBranch = Get-CurrentBranch
    $hasGit = Test-HasGit
    $featureDir = Get-FeatureDir -RepoRoot $repoRoot -Branch $currentBranch
    
    [PSCustomObject]@{
        REPO_ROOT     = $repoRoot
        CURRENT_BRANCH = $currentBranch
        HAS_GIT       = $hasGit
        FEATURE_DIR   = $featureDir
        FEATURE_SPEC  = Join-Path $featureDir 'spec.md'
        IMPL_PLAN     = Join-Path $featureDir 'plan.md'
        TASKS         = Join-Path $featureDir 'tasks.md'
        RESEARCH      = Join-Path $featureDir 'research.md'
        DATA_MODEL    = Join-Path $featureDir 'data-model.md'
        QUICKSTART    = Join-Path $featureDir 'quickstart.md'
        CONTRACTS_DIR = Join-Path $featureDir 'contracts'
    }
}

function Test-FileExists {
    param([string]$Path, [string]$Description)
    if (Test-Path -Path $Path -PathType Leaf) {
        Write-Output "  ✓ $Description"
        return $true
    } else {
        Write-Output "  ✗ $Description"
        return $false
    }
}

function Test-DirHasFiles {
    param([string]$Path, [string]$Description)
    if ((Test-Path -Path $Path -PathType Container) -and (Get-ChildItem -Path $Path -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer } | Select-Object -First 1)) {
        Write-Output "  ✓ $Description"
        return $true
    } else {
        Write-Output "  ✗ $Description"
        return $false
    }
}

function Get-TemplateCandidates {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemplateName,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    if ($TemplateName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        throw "Invalid template name '$TemplateName'."
    }

    $fileName = "$TemplateName.md"
    $specifyRoot = Join-Path $RepoRoot '.specify'
    $candidates = [System.Collections.Generic.List[string]]::new()

    $override = Join-Path $specifyRoot "templates/overrides/$fileName"
    if (Test-Path -LiteralPath $override -PathType Leaf) {
        $candidates.Add($override)
    }

    foreach ($layerRoot in @(
        (Join-Path $specifyRoot 'presets'),
        (Join-Path $specifyRoot 'extensions')
    )) {
        if (Test-Path -LiteralPath $layerRoot -PathType Container) {
            foreach ($layer in (Get-ChildItem -LiteralPath $layerRoot -Directory | Sort-Object Name)) {
                $layerTemplate = Join-Path $layer.FullName "templates/$fileName"
                if (Test-Path -LiteralPath $layerTemplate -PathType Leaf) {
                    $candidates.Add($layerTemplate)
                }
            }
        }
    }

    $sharedTemplate = Join-Path $specifyRoot "templates/$fileName"
    if (Test-Path -LiteralPath $sharedTemplate -PathType Leaf) {
        $candidates.Add($sharedTemplate)
    }

    return $candidates.ToArray()
}

function Resolve-Template {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemplateName,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $candidates = @(Get-TemplateCandidates -TemplateName $TemplateName -RepoRoot $RepoRoot)
    if ($candidates.Count -gt 0) {
        return $candidates[0]
    }

    return $null
}

function Resolve-TemplateContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemplateName,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $templatePath = Resolve-Template -TemplateName $TemplateName -RepoRoot $RepoRoot
    if ($null -eq $templatePath) {
        return $null
    }

    return [string](Get-Content -LiteralPath $templatePath -Raw)
}
