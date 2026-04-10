Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient

function Invoke-Mcp([string]$method, [object]$paramsObj) {
  $id = [int](Get-Random -Minimum 1000 -Maximum 9999)
  $payloadObj = [ordered]@{ jsonrpc='2.0'; id=$id; method=$method; params=$paramsObj }
  $payload = $payloadObj | ConvertTo-Json -Depth 20 -Compress
  $content = New-Object System.Net.Http.StringContent($payload,[System.Text.Encoding]::UTF8,'application/json')
  $resp = $client.PostAsync('http://127.0.0.1:21329/',$content).GetAwaiter().GetResult()
  $txt = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
  if([string]::IsNullOrWhiteSpace($txt)) { return @{ status=[int]$resp.StatusCode; raw=''; obj=$null } }
  try { $obj = $txt | ConvertFrom-Json } catch { $obj = $null }
  return @{ status=[int]$resp.StatusCode; raw=$txt; obj=$obj }
}

$init = Invoke-Mcp 'initialize' @{ protocolVersion='2024-11-05'; capabilities=@{}; clientInfo=@{ name='full-test-runner'; version='1.0.0' } }
$lst = Invoke-Mcp 'tools/list' @{}
$toolNames = @()
if($lst.obj -and $lst.obj.result -and $lst.obj.result.tools){ $toolNames = @($lst.obj.result.tools | ForEach-Object { $_.name }) }

$calls = @(
  @{ n='xpp_create_object'; a=@{ objectType='AxClass'; objectName='TmpCopilotSmokeClass' } },
  @{ n='xpp_read_object'; a=@{ objectType='AxClass'; objectName='TmpCopilotSmokeClass' } },
  @{ n='xpp_update_object'; a=@{ objectType='AxClass'; objectName='TmpCopilotSmokeClass'; methods=@('public static void smoke() { }') } },
  @{ n='xpp_delete_object'; a=@{ objectType='AxClass'; objectName='TmpCopilotSmokeClass' } },
  @{ n='xpp_find_object'; a=@{ objectName='Cust'; exactMatch=$false } },
  @{ n='xpp_list_objects'; a=@{ maxResults=5 } },
  @{ n='xpp_get_model_info'; a=@{ modelName='ApplicationSuite' } },
  @{ n='xpp_list_models'; a=@{} },
  @{ n='xpp_read_label'; a=@{ labelFileId='Sys'; language='en-US'; maxResults=3 } },
  @{ n='xpp_create_label'; a=@{ labelFileId='Sys'; language='en-US'; labelId='@SYS999999'; text='Smoke test' } },
  @{ n='xpp_add_to_project'; a=@{ objectType='AxClass'; objectName='TmpCopilotSmokeClass' } },
  @{ n='xpp_list_project_items'; a=@{} },
  @{ n='xpp_get_environment'; a=@{} },
  @{ n='xpp_search_docs'; a=@{ query='x++ class declaration'; maxLength=800 } }
)

$results = @()
foreach($c in $calls){
  $r = Invoke-Mcp 'tools/call' @{ name=$c.n; arguments=$c.a }
  $isErr = $null
  $msg = ''
  if($r.obj -and $r.obj.result -and $r.obj.result.content){
    $isErr = $r.obj.result.isError
    if($r.obj.result.content -is [System.Array]){
      $msg = [string]$r.obj.result.content[0].text
    } else {
      $msg = [string]$r.obj.result.content.text
    }
  } elseif($r.obj -and $r.obj.error){
    $isErr = $true
    $msg = [string]$r.obj.error.message
  } else {
    $isErr = $true
    $msg = 'No parsed response content'
  }
  if($msg.Length -gt 140){ $msg = $msg.Substring(0,140) + '...' }
  $results += [pscustomobject]@{ Tool=$c.n; HttpStatus=$r.status; IsError=$isErr; Message=$msg }
}

"InitializeStatus=$($init.status)"
"DiscoveredToolCount=$($toolNames.Count)"
"DiscoveredTools=$($toolNames -join ',')"
$results | Format-Table -AutoSize
