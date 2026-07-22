# delete-retired-files.ps1
# Physically deletes files that were retired via the AIMonitor review flow.
#   - BaseViewGenerator.razor / .css : already blanked and accepted (build passed) -> safe to delete.
#   - DatabaseAsyncRepository.cs      : verified unreferenced (no DI registration, no @inject,
#                                       no 'new'); git-tracked, so 'git checkout' restores it if needed.
# Run from the solution root (this script anchors to its own location).

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$files = @(
    'Components\Pages\BaseViewGenerator\BaseViewGenerator.razor',
    'Components\Pages\BaseViewGenerator\BaseViewGenerator.razor.css',
    'SchemaStudio.Data\Repositories\DatabaseAsyncRepository.cs'
)

foreach ($f in $files) {
    if (Test-Path -LiteralPath $f) {
        Remove-Item -LiteralPath $f -Force
        Write-Host "Deleted  $f" -ForegroundColor Green
    }
    else {
        Write-Host "Skipped  $f (not found)" -ForegroundColor Yellow
    }
}

# Remove the BaseViewGenerator folder if it is now empty.
$dir = 'Components\Pages\BaseViewGenerator'
if ((Test-Path -LiteralPath $dir) -and -not (Get-ChildItem -LiteralPath $dir -Force)) {
    Remove-Item -LiteralPath $dir -Force
    Write-Host "Removed empty folder $dir" -ForegroundColor Green
}

Write-Host "Done. Rebuild the solution to confirm the build is still green." -ForegroundColor Cyan
