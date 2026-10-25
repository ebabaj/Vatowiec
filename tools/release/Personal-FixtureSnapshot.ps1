# Read-only SQL checks, exclusively for the new root owned by the installer test.
function ReadFixtureConfiguration {
    $parent=Join-Path $env:LOCALAPPDATA 'Vatowiec'
    if([IO.Path]::GetDirectoryName($root) -ne $parent -or (Split-Path $root -Leaf) -notmatch '^Personal\.WizardTest\.[0-9a-f]{32}$'){throw 'Not an isolated fixture root.'}
    $state=Get-Content (Join-Path $root 'installation.json') -Raw | ConvertFrom-Json
    $identity=[guid]::Parse($state.InstallationId).ToString('N')
    $prefix='Vatowiec_Personal_'+$identity
    $snapshot=[ordered]@{}
    foreach($part in @('System','Company')){
        $connection=New-Object Data.SqlClient.SqlConnection
        # Windows PowerShell 5.1's legacy System.Data.SqlClient cannot request TLS on LocalDB.
        # Read-only test IPC, exact newly owned LocalDB instance only; never SQL Express/LAN.
        # The application's Microsoft.Data.SqlClient configuration and HTTPS/TLS remain unchanged.
        $connection.ConnectionString='Server=(localdb)\VatowiecPersonal_'+$identity+';Database='+$prefix+'_'+$part+';Integrated Security=True;Encrypt=False;Pooling=False;Connect Timeout=15'
        try {
            $connection.Open(); $command=$connection.CreateCommand()
            $command.CommandText="SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Owner' AND SERVERPROPERTY('IsLocalDB')=1 AND SERVERPROPERTY('ProductMajorVersion')=N'17' AND DB_NAME()=@database"
            $null=$command.Parameters.AddWithValue('@database',$prefix+'_'+$part)
            if($command.ExecuteScalar() -ne $identity){throw 'Fixture ownership/server/database mismatch.'}
            $queries=if($part -eq 'System'){@{
                Policy='SELECT IsEnabled,MinimumLength,RequireUppercase,RequireLowercase,RequireDigit,RequireSpecialCharacter,PreventPasswordReuse,EnforcePasswordExpiration,CONVERT(varchar(18),CONVERT(varbinary(8),RowVersion),1) AS Version FROM security.PasswordPolicy';
                Registries='SELECT Source,Environment,ProductionUrl,ProductionWl2Url,CONVERT(varchar(18),CONVERT(varbinary(8),RowVersion),1) AS Version FROM security.RegistryConfigurations ORDER BY Source';
                PolicyAudit='SELECT COUNT(*) AS Count FROM audit.PasswordPolicyChanges'
            }}else{@{
                Currencies='SELECT Code,IsActive,DownloadA,DownloadB,DownloadC,CONVERT(varchar(18),CONVERT(varbinary(8),RowVersion),1) AS Version FROM ref.Currencies ORDER BY Code';
                Rates='SELECT COUNT(*) AS Count FROM core.ExchangeRates';
                CurrencyAudit='SELECT COUNT(*) AS Count FROM audit.CurrencyOperations';
                Verifications='SELECT COUNT(*) AS Count FROM audit.ExternalVerifications'
            }}
            foreach($name in ($queries.Keys | Sort-Object)){
                $command.Parameters.Clear(); $command.CommandText=$queries[$name]
                $reader=$command.ExecuteReader(); $rows=[Collections.Generic.List[object]]::new()
                try{while($reader.Read()){$row=[ordered]@{}; for($i=0;$i -lt $reader.FieldCount;$i++){$row[$reader.GetName($i)]=if($reader.IsDBNull($i)){$null}else{$reader.GetValue($i)}}; $rows.Add([pscustomobject]$row)}}finally{$reader.Dispose()}
                $snapshot[$name]=$rows.ToArray()
            }
        } finally { $connection.Dispose() }
    }
    return [pscustomobject]$snapshot
}
