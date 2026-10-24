#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PfxPath,
    [Parameter(Mandatory)][string]$ApiAccount,
    [Parameter(Mandatory)][string]$ApiExecutable,
    [Parameter(Mandatory)][string]$LocalAddress,
    [Parameter(Mandatory)][ValidateRange(1024,65535)][int]$Port,
    [Parameter(Mandatory)][string]$RemoteSubnet,
    [switch]$SetNetworkPrivate
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$pfx = Get-Item -LiteralPath $PfxPath
if ($pfx.PSIsContainer -or $pfx.Extension -ne '.pfx' -or ($pfx.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Expected a regular PFX file.' }
$exe = Get-Item -LiteralPath $ApiExecutable
if ($exe.PSIsContainer -or $exe.Name -ne 'Vatowiec.Api.exe') { throw 'Expected the existing API executable.' }
$address = [Net.IPAddress]::Parse($LocalAddress)
if ($address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or [Net.IPAddress]::IsLoopback($address)) { throw 'A specific LAN IPv4 address is required.' }
if ($RemoteSubnet -notmatch '^\d{1,3}(\.\d{1,3}){3}/([1-9]|[12][0-9]|3[0-2])$') { throw 'Expected an explicit IPv4 CIDR subnet.' }
$subnetParts = $RemoteSubnet.Split('/')
[Net.IPAddress]::Parse($subnetParts[0]) | Out-Null
$interfaces = @(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $LocalAddress)
if ($interfaces.Count -ne 1) { throw 'LAN address must belong to exactly one local interface.' }
$networkProfile = Get-NetConnectionProfile -InterfaceIndex $interfaces[0].InterfaceIndex
if ($networkProfile.NetworkCategory -ne 'Private' -and -not $SetNetworkPrivate) {
    throw 'The interface is not Private. Explicitly approve -SetNetworkPrivate for this trusted LAN before applying the rule.'
}
if ($networkProfile.NetworkCategory -eq 'DomainAuthenticated') { throw 'Do not change a domain-managed network profile with this script.' }
$apiSid = [Security.Principal.NTAccount]::new($ApiAccount).Translate([Security.Principal.SecurityIdentifier])
$adminsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$ruleName = 'Vatowiec-Development-LAN-HTTPS'
$oldRule = Get-NetFirewallRule -Name $ruleName -PolicyStore PersistentStore -ErrorAction SilentlyContinue
if ($oldRule -and $oldRule.Group -ne 'Vatowiec Development HTTPS') { throw 'A firewall rule with this name belongs to another configuration.' }
$firewallProfile = Get-NetFirewallProfile -Name Private
if ($firewallProfile.Enabled -ne 'True') { throw 'Private-profile Windows Firewall must be enabled.' }

# Store ACL metadata only, outside the repository. No certificate/key is copied.
$backupDir = Join-Path $env:LOCALAPPDATA 'Vatowiec\Deployment'
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
$oldAcl = Get-Acl -LiteralPath $pfx.FullName
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-ffff')
@{ Path = $pfx.FullName; Sddl = $oldAcl.Sddl; NetworkInterfaceIndex = $interfaces[0].InterfaceIndex;
   PreviousNetworkCategory = $networkProfile.NetworkCategory.ToString() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backupDir "https-acl-before-$stamp.json") -Encoding UTF8

# A protected allowlist: no inherited Authenticated Users / Users access.
$acl = [Security.AccessControl.FileSecurity]::new()
$acl.SetOwner($adminsSid)
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @($adminsSid,$systemSid)) {
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow))
}
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($apiSid,[Security.AccessControl.FileSystemRights]::Read,[Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $pfx.FullName -AclObject $acl
$actual = Get-Acl -LiteralPath $pfx.FullName
$allowedSids = @($apiSid.Value,$adminsSid.Value,$systemSid.Value)
if (-not $actual.AreAccessRulesProtected) { throw 'PFX ACL inheritance is still enabled.' }
foreach ($entry in $actual.Access) {
    $sid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    if ($sid -notin $allowedSids -or $entry.AccessControlType -ne 'Allow' -or $entry.IsInherited) { throw 'PFX ACL verification failed.' }
}

$parameters = @{
    Name = $ruleName; DisplayName = 'VATowiec Development HTTPS - Private LAN only';
    Group = 'Vatowiec Development HTTPS'; Description = 'Explicit local address, API executable and source subnet; no Public/Domain profile or edge traversal.';
    Direction = 'Inbound'; Action = 'Allow'; Enabled = 'True'; Profile = 'Private';
    Protocol = 'TCP'; LocalAddress = $LocalAddress; LocalPort = $Port;
    RemoteAddress = $RemoteSubnet; Program = $exe.FullName;
    InterfaceAlias = $interfaces[0].InterfaceAlias; EdgeTraversalPolicy = 'Block';
    PolicyStore = 'PersistentStore'
}
if ($oldRule) {
    # Update only this script's dedicated rule; do not change unrelated rules.
    $parameters.Remove('DisplayName')
    $parameters.Remove('Group')
    $parameters.NewDisplayName = 'VATowiec Development HTTPS - Private LAN only'
    Set-NetFirewallRule @parameters | Out-Null
} else {
    New-NetFirewallRule @parameters | Out-Null
}
if ($networkProfile.NetworkCategory -ne 'Private') {
    Set-NetConnectionProfile -InterfaceIndex $interfaces[0].InterfaceIndex -NetworkCategory Private
}
Write-Output 'PFX secured: API account read-only, Administrators and SYSTEM full control. Inheritance disabled.'
Write-Output 'win-acme must renew/export as Administrator or SYSTEM and reapply this ACL if it replaces the file. The PFX path has not changed.'
Write-Output 'Dedicated firewall rule applied: Private, TCP, explicit LAN address/interface and source subnet, API executable only.'
Get-NetConnectionProfile -InterfaceIndex $interfaces[0].InterfaceIndex | Select-Object InterfaceAlias,NetworkCategory
