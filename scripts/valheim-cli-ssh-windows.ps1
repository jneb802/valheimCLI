param(
    [Parameter(Mandatory = $true)]
    [string] $HostName,

    [string] $User = "",
    [string] $ExePath = "C:\Users\USERNAME\tools\valheim-cli\valheim-cli.exe",
    [string] $GamePath = "",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CliArgs
)

$target = if ([string]::IsNullOrWhiteSpace($User)) { $HostName } else { "$User@$HostName" }
$payload = @{
    exe = $ExePath
    gamePath = $GamePath
    args = $CliArgs
} | ConvertTo-Json -Compress

$encodedPayload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
$remote = @"
`$payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$encodedPayload')) | ConvertFrom-Json
`$argsList = @()
if (-not [string]::IsNullOrWhiteSpace(`$payload.gamePath)) {
  `$argsList += '--game-path'
  `$argsList += `$payload.gamePath
}
foreach (`$arg in `$payload.args) { `$argsList += `$arg }
& `$payload.exe @argsList
"@

$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($remote))
ssh $target "powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
