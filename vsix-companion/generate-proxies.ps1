$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sample = Join-Path $root 'Samples\Microsoft.Dynamics.FinOps.ToolsVS2022'

[void][System.Reflection.Assembly]::LoadFrom((Join-Path $sample 'Microsoft.Dynamics.AX.Metadata.Core.dll'))
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $sample 'Microsoft.Dynamics.AX.Metadata.dll'))
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $sample 'Microsoft.Dynamics.AX.Metadata.Storage.dll'))

$typeMap = [ordered]@{
    AxClass = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxClass'
    AxTable = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxTable'
    AxForm = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxForm'
    AxEdt = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxEdt'
    AxEnum = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxEnum'
    AxView = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxView'
    AxQuery = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxQuerySimple'
    AxMap = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMap'
    AxMenu = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenu'
    AxTile = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxTile'
    AxMenuItemDisplay = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemDisplay'
    AxMenuItemOutput = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemOutput'
    AxMenuItemAction = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemAction'
    AxDataEntityView = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxDataEntityView'
    AxSecurityPrivilege = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityPrivilege'
    AxSecurityDuty = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityDuty'
    AxSecurityRole = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityRole'
    AxService = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxService'
    AxServiceGroup = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxServiceGroup'
    AxConfigurationKey = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxConfigurationKey'
    AxTableExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxTableExtension'
    AxFormExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxFormExtension'
    AxEnumExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxEnumExtension'
    AxEdtExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxEdtExtension'
    AxViewExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxViewExtension'
    AxMenuExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuExtension'
    AxMenuItemDisplayExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemDisplayExtension'
    AxMenuItemOutputExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemOutputExtension'
    AxMenuItemActionExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxMenuItemActionExtension'
    AxQuerySimpleExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxQuerySimpleExtension'
    AxSecurityDutyExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityDutyExtension'
    AxSecurityRoleExtension = 'Microsoft.Dynamics.AX.Metadata.MetaModel.AxSecurityRoleExtension'
}

function Resolve-LoadedType([string]$fullName) {
    $direct = [Type]::GetType($fullName, $false)
    if ($null -ne $direct) { return $direct }

    foreach ($asm in [AppDomain]::CurrentDomain.GetAssemblies()) {
        $t = $asm.GetType($fullName, $false)
        if ($null -ne $t) { return $t }
    }

    return $null
}

$resolved = [ordered]@{}
foreach ($k in $typeMap.Keys) {
    $t = Resolve-LoadedType $typeMap[$k]
    if ($null -ne $t) { $resolved[$k] = $t }
}

function Is-SimpleType([Type]$t) {
    if ($null -eq $t) { return $true }
    $u = [Nullable]::GetUnderlyingType($t)
    if ($null -ne $u) { $t = $u }
    return $t.IsPrimitive -or $t.IsEnum -or $t -eq [string] -or $t -eq [decimal] -or $t -eq [DateTime] -or $t -eq [Guid]
}

function Map-SimpleType([Type]$t) {
    $u = [Nullable]::GetUnderlyingType($t)
    if ($null -ne $u) { $t = $u }

    if ($t -eq [string]) { return 'string' }
    if ($t -eq [bool]) { return 'bool' }
    if ($t -eq [byte]) { return 'byte' }
    if ($t -eq [Int16]) { return 'short' }
    if ($t -eq [Int32]) { return 'int' }
    if ($t -eq [Int64]) { return 'long' }
    if ($t -eq [single]) { return 'float' }
    if ($t -eq [double]) { return 'double' }
    if ($t -eq [decimal]) { return 'decimal' }
    if ($t -eq [DateTime]) { return 'DateTime' }
    if ($t -eq [Guid]) { return 'Guid' }
    if ($t.IsEnum) { return 'string' }
    return 'object'
}

function Get-EnumerableItemType([Type]$t) {
    if ($t.IsArray) { return $t.GetElementType() }

    if ($t.IsGenericType) {
        $args = $t.GetGenericArguments()
        if ($args.Length -eq 1) { return $args[0] }
    }

    foreach ($i in $t.GetInterfaces()) {
        if ($i.IsGenericType -and $i.GetGenericTypeDefinition().FullName -eq 'System.Collections.Generic.IEnumerable`1') {
            return $i.GetGenericArguments()[0]
        }
    }

    return $null
}

function Sanitize-Name([string]$n) {
    if ([string]::IsNullOrWhiteSpace($n)) { return 'MetadataProxy' }

    $sb = New-Object System.Text.StringBuilder
    if (-not [char]::IsLetter($n[0]) -and $n[0] -ne '_') { [void]$sb.Append('_') }

    foreach ($c in $n.ToCharArray()) {
        if ([char]::IsLetterOrDigit($c) -or $c -eq '_') { [void]$sb.Append($c) }
        else { [void]$sb.Append('_') }
    }

    return $sb.ToString()
}

$typeToName = @{}
$usedNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$classes = New-Object System.Collections.ArrayList

function Get-UniqueName([string]$name) {
    $candidate = Sanitize-Name $name
    if ($usedNames.Add($candidate)) { return $candidate }

    $i = 2
    while (-not $usedNames.Add($candidate + $i)) { $i++ }
    return $candidate + $i
}

function Ensure-ProxyType([Type]$t, [string]$preferredName) {
    if ($null -eq $t) { return 'object' }

    $u = [Nullable]::GetUnderlyingType($t)
    if ($null -ne $u) { $t = $u }

    if (Is-SimpleType $t) { return (Map-SimpleType $t) }

    if ($t -ne [string] -and [System.Collections.IEnumerable].IsAssignableFrom($t)) {
        $itemType = Get-EnumerableItemType $t
        if ($null -eq $itemType) { $itemType = [object] }
        return 'List<' + (Ensure-ProxyType $itemType ($itemType.Name + 'Proxy')) + '>'
    }

    $key = $t.AssemblyQualifiedName
    if ($typeToName.ContainsKey($key)) { return $typeToName[$key] }

    $className = Get-UniqueName $preferredName
    $typeToName[$key] = $className

    $props = New-Object System.Collections.ArrayList
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance

    foreach ($p in $t.GetProperties($flags)) {
        if (-not $p.CanRead) { continue }
        if ($p.GetIndexParameters().Length -gt 0) { continue }
        if ($p.Name -ieq 'Parent' -or $p.Name -ieq 'Owner') { continue }

        $proxyTypeName = Ensure-ProxyType $p.PropertyType ($p.Name + 'Proxy')
        [void]$props.Add([pscustomobject]@{
            Name = (Sanitize-Name $p.Name)
            Source = $p.Name
            TypeName = $proxyTypeName
            CanWrite = $p.CanWrite
        })
    }

    [void]$classes.Add([pscustomobject]@{
        Name = $className
        Source = $t.Name
        Properties = $props
    })

    return $className
}

foreach ($k in $resolved.Keys) {
    [void](Ensure-ProxyType $resolved[$k] ($k + 'Proxy'))
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('using System;')
[void]$sb.AppendLine('using System.Collections.Generic;')
[void]$sb.AppendLine()
[void]$sb.AppendLine('namespace XppAiCopilotCompanion.MetaModel.Generated')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    /// <summary>')
[void]$sb.AppendLine('    /// Auto-generated static proxy contracts for D365FO metadata types.')
[void]$sb.AppendLine('    /// Generated from FinOps metadata assemblies in Samples.')
[void]$sb.AppendLine('    /// </summary>')
[void]$sb.AppendLine('    public static class MetadataProxyManifest')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        public static readonly string[] RootObjectTypes = new[]')
[void]$sb.AppendLine('        {')

$rootKeys = [string[]]$resolved.Keys
for ($i = 0; $i -lt $rootKeys.Length; $i++) {
    $comma = if ($i -lt ($rootKeys.Length - 1)) { ',' } else { '' }
    [void]$sb.AppendLine('            "' + $rootKeys[$i] + '"' + $comma)
}

[void]$sb.AppendLine('        };')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine()

foreach ($c in ($classes | Sort-Object Name)) {
    [void]$sb.AppendLine('    public sealed class ' + $c.Name)
    [void]$sb.AppendLine('    {')

    foreach ($p in ($c.Properties | Sort-Object Name)) {
        $setter = 'set;'
        [void]$sb.AppendLine('        public ' + $p.TypeName + ' ' + $p.Name + ' { get; ' + $setter + ' }')
    }

    [void]$sb.AppendLine('    }')
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine('}')

$outDir = Join-Path $root 'MetaModel\Generated'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$outFile = Join-Path $outDir 'MetadataProxies.g.cs'
[System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.Encoding]::UTF8)

Write-Host ('GENERATED=' + $outFile)
Write-Host ('ROOTS=' + $resolved.Keys.Count + ' CLASSES=' + $classes.Count)
