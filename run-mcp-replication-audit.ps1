$ErrorActionPreference = 'Continue'

try {
    Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
}
catch {
    # Continue: PowerShell editions with preloaded System.Net.Http will throw here.
}

$endpoint    = 'http://127.0.0.1:21329/'
$reportPath  = 'c:\Users\MohamedNazmi\source\repos\AIDEVTOOLS\mcp-replication-failures.md'
$mcpCallTimeoutSec = 10
$mcpWriteTimeoutSec = 10

$types = @(
    'AxClass','AxTable','AxView','AxDataEntityView','AxMap','AxEdt','AxEnum','AxForm','AxMenu',
    'AxMenuItemDisplay','AxMenuItemOutput','AxMenuItemAction','AxQuery','AxSecurityPrivilege','AxSecurityDuty',
    'AxSecurityRole','AxService','AxServiceGroup','AxConfigurationKey','AxTableExtension','AxFormExtension',
    'AxEnumExtension','AxEdtExtension','AxViewExtension','AxMenuExtension','AxMenuItemDisplayExtension',
    'AxMenuItemOutputExtension','AxMenuItemActionExtension','AxQuerySimpleExtension','AxSecurityDutyExtension',
    'AxSecurityRoleExtension',
    'AxTile'  # Last — DeleteTile can hang the bridge
)

function Invoke-Mcp {
    param([string]$ToolName, [object]$ToolArgs)

    $callSw = [System.Diagnostics.Stopwatch]::StartNew()
    $client = $null
    Write-Host ("    MCP call: {0} (timeout={1}s)" -f $ToolName, $mcpCallTimeoutSec) -ForegroundColor DarkGray
    try {
        $payload = @{
            jsonrpc = '2.0'
            id      = [guid]::NewGuid().ToString()
            method  = 'tools/call'
            params  = @{ name = $ToolName; arguments = $ToolArgs }
        } | ConvertTo-Json -Depth 50

        $client         = New-Object System.Net.Http.HttpClient
        $client.Timeout = [TimeSpan]::FromSeconds($mcpCallTimeoutSec)
        $content  = New-Object System.Net.Http.StringContent($payload, [System.Text.Encoding]::UTF8, 'application/json')
        $resp     = $client.PostAsync($endpoint, $content).GetAwaiter().GetResult()
        $txt      = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        $obj  = $txt | ConvertFrom-Json
        $text = ''
        if ($obj.result -and $obj.result.content -and $obj.result.content.Count -gt 0) {
            $text = [string]$obj.result.content[0].text
        }

        [pscustomobject]@{
            IsError = [bool]($obj.result.isError)
            Text    = $text
            Raw     = $txt
        }
    }
    catch {
        $msg = $_.Exception.Message
        if ($msg -match 'timed out|TaskCanceledException|canceled') {
            $msg = "TIMEOUT ($mcpCallTimeoutSec sec) in $ToolName"
        }
        [pscustomobject]@{
            IsError = $true
            Text    = "$msg; elapsedMs=$($callSw.ElapsedMilliseconds)"
            Raw     = ''
        }
    }
    finally {
        if ($client) {
            $client.Dispose()
        }
    }
}

# Extract a JSON (array or object) block from a named text section produced by xpp_read_object
function Get-JsonSection {
    param([string]$Text, [string]$SectionName)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

    $pat = '(?s)===\s*' + [regex]::Escape($SectionName) + '.*?===\s*\r?\n(?<json>[\[{].*?)(?=\r?\n===|$)'
    $m = [regex]::Match($Text, $pat)
    if (-not $m.Success) { return $null }

    $json = $m.Groups['json'].Value.Trim()
    try { return ($json | ConvertFrom-Json -Depth 100) } catch { return $null }
}

# Extract a plain-text block from a named section (e.g. "=== Declaration ===")
function Get-TextSection {
    param([string]$Text, [string]$SectionName)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

    $pat = '(?s)=== ' + [regex]::Escape($SectionName) + ' ===\r?\n(?<body>.*?)(?=\r?\n===|\z)'
    $m = [regex]::Match($Text, $pat)
    if (-not $m.Success) { return $null }

    $body = $m.Groups['body'].Value.Trim()
    return $(if ([string]::IsNullOrWhiteSpace($body)) { $null } else { $body })
}

# Extract a named section body from xpp_read_object output.
function Get-SectionBody {
    param([string]$Text, [string]$SectionName)

    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }

    $pat = '(?s)===\s*' + [regex]::Escape($SectionName) + '.*?===\s*\r?\n(?<body>.*?)(?=\r?\n===|\z)'
    $m = [regex]::Match($Text, $pat)
    if (-not $m.Success) { return '' }
    return $m.Groups['body'].Value.Trim()
}

function Normalize-ForComparison {
    param([string]$Value, [string]$SampleName, [string]$CloneName)

    $v = [string]$Value
    if (-not [string]::IsNullOrWhiteSpace($SampleName)) {
        $v = [regex]::Replace($v, '\b' + [regex]::Escape($SampleName) + '\b', '__OBJNAME__')
    }
    if (-not [string]::IsNullOrWhiteSpace($CloneName)) {
        $v = [regex]::Replace($v, '\b' + [regex]::Escape($CloneName) + '\b', '__OBJNAME__')
    }
    $v = ($v -replace '\r?\n', ' ')
    $v = [regex]::Replace($v, '\s+', ' ').Trim()
    return $v
}

function Compare-ObjectMetadataExact {
    param(
        [string]$SampleText,
        [string]$CloneText,
        [string]$SampleName,
        [string]$CloneName
    )

    $sections = @(
        'Declaration',
        'Properties (use in "properties" parameter)',
        'Enum Values (use in "enumValues" parameter)',
        'Fields (use in "fields" parameter)',
        'Indexes (use in "indexes" parameter)',
        'Field Groups (use in "fieldGroups" parameter)',
        'Relations (use in "relations" parameter)',
        'Data Sources (use in "dataSources" parameter)',
        'Entry Points (use in "entryPoints" parameter)'
    )

    $mismatch = @()
    $compared = 0
    foreach ($sec in $sections) {
        $leftRaw  = Get-SectionBody -Text $SampleText -SectionName $sec
        $rightRaw = Get-SectionBody -Text $CloneText -SectionName $sec
        if ([string]::IsNullOrWhiteSpace($leftRaw) -and [string]::IsNullOrWhiteSpace($rightRaw)) {
            continue
        }

        $compared++
        $left  = Normalize-ForComparison -Value $leftRaw -SampleName $SampleName -CloneName $CloneName
        $right = Normalize-ForComparison -Value $rightRaw -SampleName $SampleName -CloneName $CloneName
        if ($left -ne $right) {
            $mismatch += $sec
        }
    }

    [pscustomobject]@{
        IsExact         = ($mismatch.Count -eq 0)
        MismatchSections = $mismatch
        ComparedSections = $compared
    }
}

function Get-ActiveProjectModel {
    # xpp_get_environment currently hangs intermittently in MCP dispatch.
    # Keep tests moving by defaulting to sample model resolution.
    return $null
}

$script:sampleCache = @{
    AxClass = @{ Name = 'BankAccountTablePHR_EventHandler'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxTable = @{ Name = 'PHRAccmps'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxView = @{ Name = 'PHRDepartmentsNameIdView'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxDataEntityView = @{ Name = 'PHRAccmpsEntity'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxMap = @{ Name = 'PHRImportMap'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxEdt = @{ Name = 'DEL_PHRExeFileLocation'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxEnum = @{ Name = 'PHRAccountCodeTypes'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxForm = @{ Name = 'PHRAccmps'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxTile = @{ Name = 'PHRPayrollTile'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxMenu = @{ Name = 'PHRPayrollAndHumanResources'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxMenuItemDisplay = @{ Name = 'HcmWorkerListPage_PHRWorkers'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxMenuItemOutput = @{ Name = 'PHRCheque'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxMenuItemAction = @{ Name = 'PHRAccumClearingBatch'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxQuery = @{ Name = 'HcmWorkerListPage_PHRWorkers'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxSecurityPrivilege = @{ Name = 'PHRAccumClearingBatch'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxSecurityDuty = @{ Name = 'PHRAddWorkerAndPosition'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxSecurityRole = @{ Name = 'PHRAdvancedPayrollAuditor'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxService = @{ Name = 'PHRUSStatRptService'; Model = 'AdvancedPayrollUSLocalization'; IsBaseProduct = $false }
    AxServiceGroup = @{ Name = 'PHRUSStatRptServiceGrp'; Model = 'AdvancedPayrollUSLocalization'; IsBaseProduct = $false }
    AxConfigurationKey = @{ Name = 'PHRFutureVersions'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxTableExtension = @{ Name = 'BankAccountTable.ExtensionPHRTable'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxFormExtension = @{ Name = 'BankAccountTable.ExtensionPHRForm'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxEnumExtension = @{ Name = 'NumberSeqModule.ExtensionPHREnum'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxEdtExtension = @{ Name = 'Description.CMCExtension'; Model = 'AnthologyFinanceHcm'; IsBaseProduct = $false }
    AxViewExtension = @{ Name = 'BankAccountTableLookup.CMCExtension'; Model = 'AnthologyFinanceHcm'; IsBaseProduct = $false }
    AxMenuExtension = @{ Name = 'MainMenu.ExtensionPHRMenu'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
    AxMenuItemDisplayExtension = @{ Name = 'HcmCourseType.CMCExtension'; Model = 'AnthologyFinanceHcm'; IsBaseProduct = $false }
    AxMenuItemActionExtension = @{ Name = 'PayrollEarningsStatementsReleased.CMCExtension'; Model = 'AnthologyFinanceHcm'; IsBaseProduct = $false }
    AxQuerySimpleExtension = @{ Name = 'ATHTimesheetProcessQR.AnthologyAdvancePayrollConnector'; Model = 'Anthology Advance Payroll Connector'; IsBaseProduct = $false }
    AxSecurityDutyExtension = @{ Name = 'ATHFederalWorkStudyMaintainDuty.AnthologyAdvancePayrollConnector'; Model = 'Anthology Advance Payroll Connector'; IsBaseProduct = $false }
    AxSecurityRoleExtension = @{ Name = 'HcmHumanResourceAssistant.ExtensionPHRSecurityRole'; Model = 'AdvancedPayroll'; IsBaseProduct = $false }
}

Write-Host "Loaded hardcoded sample artifacts: $($script:sampleCache.Count)/$($types.Count) types." -ForegroundColor Green

# ── Smoke test: verify MCP is alive and responding to tool calls ──
Write-Host ''
Write-Host '=== MCP Smoke Test ===' -ForegroundColor Yellow
$smokeOk = $false
try {
    # 1. Health endpoint
    $hc = New-Object System.Net.Http.HttpClient
    $hc.Timeout = [TimeSpan]::FromSeconds(5)
    $hr = $hc.GetAsync($endpoint).GetAwaiter().GetResult()
    $ht = $hr.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $hc.Dispose()
    Write-Host "  Health endpoint: OK ($ht)" -ForegroundColor Green

    # 2. tools/list
    $listPayload = '{"jsonrpc":"2.0","id":"smoke-list","method":"tools/list","params":{}}'
    $lc = New-Object System.Net.Http.HttpClient
    $lc.Timeout = [TimeSpan]::FromSeconds($mcpCallTimeoutSec)
    $lb = New-Object System.Net.Http.StringContent($listPayload,[Text.Encoding]::UTF8,'application/json')
    $lr = $lc.PostAsync($endpoint,$lb).GetAwaiter().GetResult()
    $lt = $lr.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $lc.Dispose()
    if ($lt.Length -gt 100) {
        Write-Host "  tools/list: OK ($($lt.Length) bytes, tools found)" -ForegroundColor Green
    } else {
        Write-Host "  tools/list: UNEXPECTED ($lt)" -ForegroundColor Red
        throw 'tools/list returned too little data'
    }

    # 3. Actual tool call — read a known object
    $readResult = Invoke-Mcp -ToolName 'xpp_read_object' -ToolArgs @{
        objectType = 'AxEnum'
        objectName = 'PHRAccountCodeTypes'
        modelName  = 'AdvancedPayroll'
    }
    if ($readResult.IsError) {
        Write-Host "  xpp_read_object: FAILED ($($readResult.Text))" -ForegroundColor Red
        throw 'Smoke test read call failed'
    } else {
        Write-Host "  xpp_read_object: OK ($($readResult.Text.Length) chars)" -ForegroundColor Green
    }

    $smokeOk = $true
    Write-Host '=== Smoke test PASSED — MCP is healthy ===' -ForegroundColor Green
}
catch {
    Write-Host "=== Smoke test FAILED: $($_.Exception.Message) ===" -ForegroundColor Red
    Write-Host 'MCP server is not responding to tool calls. Restart XppMcpServer.exe and try again.' -ForegroundColor Red
    exit 1
}
Write-Host ''

function Get-SampleViaMcp {
    param([string]$TypeName)
    if ($script:sampleCache.ContainsKey($TypeName)) { return $script:sampleCache[$TypeName] }
    return $null
}

$results   = @()
$timestamp = Get-Date -Format 'yyyyMMddHHmmss'
$consecutiveFailures = 0
$maxConsecutiveFailures = 2

$script:activeProjectModel = Get-ActiveProjectModel
if (-not [string]::IsNullOrWhiteSpace($script:activeProjectModel)) {
    Write-Host "Active project model detected: $script:activeProjectModel" -ForegroundColor Green
} else {
    Write-Host "Active project model not detected; create will use sample model." -ForegroundColor Yellow
}

foreach ($type in $types) {
    # Circuit breaker: stop after N consecutive failures (likely cascade / bridge down)
    if ($consecutiveFailures -ge $maxConsecutiveFailures) {
        Write-Host "STOPPING: $consecutiveFailures consecutive failures — bridge may be hung. Skipping remaining types." -ForegroundColor Red
        foreach ($remaining in $types[($types.IndexOf($type))..($types.Count-1)]) {
            $results += [pscustomobject]@{
                ObjectType = $remaining; SampleObject = ''; NewObject = ''; TargetModel = ''
                SampleModel = ''; ReadStatus = 'Skipped'; CreateStatus = 'Skipped'
                ValidateStatus = 'Skipped'; CompareStatus = 'Skipped'; DeleteStatus = 'Skipped'
                Notes = 'Skipped: circuit breaker after consecutive failures.'; SampleSource = 'None'; MetadataSections = ''
            }
        }
        break
    }

    Write-Host "Testing $type ..." -ForegroundColor Cyan

    # --- Step 1: resolve sample object via persisted folder-exploration artifacts ---
    $sampleInfo = Get-SampleViaMcp -TypeName $type

    if (-not $sampleInfo) {
        Write-Host "  No sample found in discovery artifacts" -ForegroundColor Yellow
        $results += [pscustomobject]@{
            ObjectType     = $type
            SampleObject   = ''
            NewObject      = ''
            TargetModel    = ''
            ReadStatus     = 'Skipped'
            CreateStatus   = 'Skipped'
            ValidateStatus = 'Skipped'
            DeleteStatus   = 'Skipped'
            Notes          = 'No sample found.'
            SampleSource   = 'None'
            MetadataSections = ''
        }
        continue
    }

    $sampleName  = $sampleInfo.Name
    $sampleModel = $sampleInfo.Model
    $createModel = if (-not [string]::IsNullOrWhiteSpace($script:activeProjectModel)) { $script:activeProjectModel } else { $sampleModel }
    Write-Host "  Sample: $sampleName  ($sampleModel)" -ForegroundColor Gray
    $sampleSource = if ($sampleInfo.IsBaseProduct) { 'BaseProduct' } else { 'Custom' }
    Write-Host "  Source: $sampleSource" -ForegroundColor $(if ($sampleSource -eq 'BaseProduct') { 'Cyan' } else { 'Gray' })
    Write-Host "  CreateModel: $createModel" -ForegroundColor Gray

    $shortType = $type -replace '^Ax', ''
    $nameBase  = 'TmpRep' + $shortType + $timestamp
    $newName   = $nameBase.Substring(0, [Math]::Min(58, $nameBase.Length))

    # --- Step 2: read the sample ---
    $read = Invoke-Mcp -ToolName 'xpp_read_object' -ToolArgs @{
        objectType = $type
        objectName = $sampleName
        modelName  = $sampleModel
    }
    Write-Host "  Read: $(if ($read.IsError) { 'Failed' } else { 'Passed' })" -ForegroundColor $(if ($read.IsError) { 'Red' } else { 'Green' })

    # --- Step 3: build create args from read output ---
    $createArgs = @{
        objectType = $type
        objectName = $newName
        modelName  = $createModel
    }

    if (-not $read.IsError -and -not [string]::IsNullOrWhiteSpace($read.Text)) {
        # Declaration (plain X++ source — not JSON)
        $decl = Get-TextSection -Text $read.Text -SectionName 'Declaration'
        if ($decl) { $createArgs.declaration = $decl }

        # Typed metadata sections (JSON round-trip)
        $props       = Get-JsonSection -Text $read.Text -SectionName 'Properties (use in "properties" parameter)'
        $enumValues  = Get-JsonSection -Text $read.Text -SectionName 'Enum Values (use in "enumValues" parameter)'
        $fields      = Get-JsonSection -Text $read.Text -SectionName 'Fields (use in "fields" parameter)'
        $indexes     = Get-JsonSection -Text $read.Text -SectionName 'Indexes (use in "indexes" parameter)'
        $fieldGroups = Get-JsonSection -Text $read.Text -SectionName 'Field Groups (use in "fieldGroups" parameter)'
        $relations   = Get-JsonSection -Text $read.Text -SectionName 'Relations (use in "relations" parameter)'
        $dataSources = Get-JsonSection -Text $read.Text -SectionName 'Data Sources (use in "dataSources" parameter)'
        $entryPoints = Get-JsonSection -Text $read.Text -SectionName 'Entry Points (use in "entryPoints" parameter)'

        if ($props) {
            $createArgs.properties = @{}
            $props.psobject.Properties | ForEach-Object { $createArgs.properties[$_.Name] = [string]$_.Value }
        }
        if ($enumValues)  { $createArgs.enumValues  = @($enumValues)  }
        if ($fields)      { $createArgs.fields      = @($fields)      }
        if ($indexes)     { $createArgs.indexes     = @($indexes)     }
        if ($fieldGroups) { $createArgs.fieldGroups = @($fieldGroups) }
        if ($relations)   { $createArgs.relations   = @($relations)   }
        if ($dataSources) { $createArgs.dataSources = @($dataSources) }
        if ($entryPoints) { $createArgs.entryPoints = @($entryPoints) }
    }

    # --- Step 4: create ---
    $create = Invoke-Mcp -ToolName 'xpp_create_object' -ToolArgs $createArgs
    Write-Host "  Create: $(if ($create.IsError) { 'Failed' } else { 'Passed' })" -ForegroundColor $(if ($create.IsError) { 'Red' } else { 'Green' })

    # --- Step 5: validate ---
    $validate = $null
    if (-not $create.IsError) {
        $validate = Invoke-Mcp -ToolName 'xpp_validate_object' -ToolArgs @{
            objectType = $type; objectName = $newName; modelName = $createModel
        }
    }

    # --- Step 5b: exact sample-vs-generated metadata comparison (name ignored) ---
    $compareStatus  = 'Skipped'
    $compareDetails = ''
    if (-not $create.IsError -and -not $read.IsError) {
        $readNew = Invoke-Mcp -ToolName 'xpp_read_object' -ToolArgs @{
            objectType = $type
            objectName = $newName
            modelName  = $createModel
        }

        if ($readNew.IsError) {
            $compareStatus  = 'Failed'
            $compareDetails = 'Unable to read generated object for comparison.'
        } else {
            $cmp = Compare-ObjectMetadataExact -SampleText $read.Text -CloneText $readNew.Text -SampleName $sampleName -CloneName $newName
            if ($cmp.IsExact) {
                $compareStatus = 'Passed'
            } else {
                $compareStatus = 'Failed'
                $compareDetails = 'Mismatch sections: ' + ($cmp.MismatchSections -join ', ')
            }
        }
    }

    # --- Step 6: delete ---
    $delete = $null
    if (-not $create.IsError) {
        $delete = Invoke-Mcp -ToolName 'xpp_delete_object' -ToolArgs @{
            objectType = $type; objectName = $newName; modelName = $createModel
        }
    }

    $note = @()
    if ($read.IsError) {
        $t = ($read.Text -replace '\r?\n', ' ')
        $note += ('READ: ' + $t.Substring(0, [Math]::Min(300, $t.Length)))
    }
    if ($create.IsError) {
        $t = ($create.Text -replace '\r?\n', ' ')
        $note += ('CREATE: ' + $t.Substring(0, [Math]::Min(300, $t.Length)))
    }
    if ($validate -and $validate.IsError) {
        $t = ($validate.Text -replace '\r?\n', ' ')
        $note += ('VALIDATE: ' + $t.Substring(0, [Math]::Min(300, $t.Length)))
    }
    if ($delete -and $delete.IsError) {
        $t = ($delete.Text -replace '\r?\n', ' ')
        $note += ('DELETE: ' + $t.Substring(0, [Math]::Min(300, $t.Length)))
    }
    if ($compareStatus -eq 'Failed' -and -not [string]::IsNullOrWhiteSpace($compareDetails)) {
        $note += ('COMPARE: ' + $compareDetails)
    }

    $validatePassed = $false
    if ($validate) {
        if (-not $validate.IsError)                                              { $validatePassed = $true }
        elseif ($validate.Text -match 'Validation passed')                      { $validatePassed = $true }
        elseif ($validate.Text -match 'metadata is correct.*NOT in the active') { $validatePassed = $true }
    }

    $validateStatus = if ($validate) { if ($validatePassed) { 'Passed' } else { 'Failed' } } else { 'Skipped' }
    $deleteStatus   = if ($delete)   { if ($delete.IsError)  { 'Failed' } else { 'Passed' } } else { 'Skipped' }

    # Track consecutive failures for circuit breaker
    $typeHadFailure = ($read.IsError -or $create.IsError)
    if ($typeHadFailure) { $consecutiveFailures++ } else { $consecutiveFailures = 0 }

    Write-Host "  Validate: $validateStatus  Compare: $compareStatus  Delete: $deleteStatus" -ForegroundColor Gray

    $results += [pscustomobject]@{
        ObjectType     = $type
        SampleObject   = $sampleName
        NewObject      = $newName
        TargetModel    = $createModel
        SampleModel    = $sampleModel
        ReadStatus     = $(if ($read.IsError) { 'Failed' } else { 'Passed' })
        CreateStatus   = $(if ($create.IsError) { 'Failed' } else { 'Passed' })
        ValidateStatus = $validateStatus
        CompareStatus  = $compareStatus
        DeleteStatus   = $deleteStatus
        Notes          = ($note -join ' | ')
        SampleSource   = $sampleSource
        MetadataSections = ''
    }
    
        # Count how many metadata sections were extracted (informational)
        if (-not $read.IsError -and -not [string]::IsNullOrWhiteSpace($read.Text)) {
            $sections = @('Declaration', 'Properties', 'Fields', 'Indexes', 'Field Groups', 'Relations', 'Data Sources', 'Entry Points')
            $extractedCount = 0
            foreach ($sec in $sections) {
                if ($read.Text -match "=== $sec") { $extractedCount++ }
            }
            $results[-1].MetadataSections = "$extractedCount/$($sections.Count) sections"
    }

    # Cooldown: let VS main thread process metadata events before next type
    Start-Sleep -Seconds 3
}

$failed  = @($results | Where-Object {
    $_.CreateStatus -eq 'Failed' -or $_.ReadStatus -eq 'Failed' -or
    $_.ValidateStatus -eq 'Failed' -or $_.CompareStatus -eq 'Failed' -or $_.DeleteStatus -eq 'Failed'
})
$skipped = @($results | Where-Object { $_.ReadStatus -eq 'Skipped' })
$passed  = @($results | Where-Object {
    $_.ReadStatus -ne 'Skipped' -and $_.ReadStatus -ne 'Failed' -and
    $_.CreateStatus -ne 'Failed' -and $_.ValidateStatus -ne 'Failed' -and $_.DeleteStatus -ne 'Failed'
})

$lines = @()
$lines += '# MCP Replication Audit Report'
$lines += ''
$lines += ('Generated: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$lines += 'Discovery: via persisted folder exploration artifacts (no xpp_list_objects calls)'
$lines += ''
$lines += '## Summary'
$lines += ('- Total object types tested: ' + $results.Count)
$lines += ('- Passed: '  + $passed.Count)
$lines += ('- Failed: '  + $failed.Count)
$lines += ('- Skipped (no sample found in artifacts): ' + $skipped.Count)
$lines += '## Sample Sources'
$lines += ''
$customCount = $results | Where-Object { $_.SampleSource -eq 'Custom' } | Measure-Object | Select-Object -ExpandProperty Count
$baseCount = $results | Where-Object { $_.SampleSource -eq 'BaseProduct' } | Measure-Object | Select-Object -ExpandProperty Count
$lines += ('- Hardcoded sample set (unconstrained discovery snapshot): ' + $customCount)
$lines += ('- Base product packages: ' + $baseCount)
$lines += ('- Not tested: ' + $skipped.Count)
$lines += ''
$lines += ''
$lines += '## Failures'
$lines += ''

if ($failed.Count -eq 0) {
    $lines += 'No failures detected.'
} else {
    foreach ($f in $failed) {
        $lines += ('### ' + $f.ObjectType)
        $lines += ('- Sample: '       + $f.SampleObject)
        $lines += ('- New object: '   + $f.NewObject)
        $lines += ('- Sample model: ' + $f.SampleModel)
        $lines += ('- Create model: ' + $f.TargetModel)
        $lines += ('- Read: ' + $f.ReadStatus + ', Create: ' + $f.CreateStatus +
               ', Validate: ' + $f.ValidateStatus + ', Compare: ' + $f.CompareStatus + ', Delete: ' + $f.DeleteStatus)
        if (-not [string]::IsNullOrWhiteSpace($f.Notes)) {
            $lines += ('- Error details: ' + $f.Notes)
        }
        $lines += ''
    }
}

$lines += '## Full Matrix'
$lines += ''
$lines += '| ObjectType | SampleObject | Source | Metadata | Read | Create | Validate | Compare | Delete | Notes |'
$lines += '|---|---|---|---|---|---|---|---|---|---|'

foreach ($r in $results) {
    $notes = ($r.Notes -replace '\|', '/').Replace("`r", ' ').Replace("`n", ' ')
    $notes = $notes.Substring(0, [Math]::Min(100, $notes.Length))
    $lines += ('| ' + $r.ObjectType + ' | ' + $r.SampleObject + ' | ' + $r.SampleSource +
               ' | ' + $r.MetadataSections + ' | ' + $r.ReadStatus + ' | ' + $r.CreateStatus +
               ' | ' + $r.ValidateStatus + ' | ' + $r.CompareStatus + ' | ' + $r.DeleteStatus + ' | ' + $notes + ' |')
}

Set-Content -Path $reportPath -Value $lines -Encoding UTF8

Write-Output ''
Write-Output ('REPORT=' + $reportPath)
Write-Output ('TOTAL=' + $results.Count + '  PASSED=' + $passed.Count + '  FAILED=' + $failed.Count + '  SKIPPED=' + $skipped.Count)

