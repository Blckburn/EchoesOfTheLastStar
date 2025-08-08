param(
  [string]$Repo = "Blckburn/EchoesOfTheLastStar"
)

$docPath = Join-Path $PSScriptRoot "..\docs\tasks\DAY_01.md"
if (!(Test-Path $docPath)) { Write-Error "DAY_01.md not found"; exit 1 }

# Ensure gh
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (!$gh) { Write-Error "GitHub CLI (gh) not found"; exit 1 }

$content = Get-Content $docPath -Raw
# Split by headings starting with ### Txx —
$tasks = Select-String -InputObject $content -Pattern "(?ms)^###\s+(T\d+)[^\n]*\n(.*?)(?=\n###|\z)" -AllMatches

foreach ($m in $tasks.Matches) {
  $key = $m.Groups[1].Value
  $body = $m.Groups[2].Value.Trim()
  $titleLine = ($body -split "\r?\n")[0]
  # Build title from first line after heading or use key
  $title = "$key — " + ($titleLine.Replace('#','').Trim())
  $issueBody = "````markdown`n$m`n````"
  gh issue create --repo $Repo --title "$title" --body "$issueBody" --label "day1,task" | Out-Null
  Write-Host "Created issue: $title"
}
