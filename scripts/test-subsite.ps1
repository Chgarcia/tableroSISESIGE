param([string]$BaseUrl = 'https://servidor.institucional.gob.mx/TableroSISESIGE/')
$BaseUrl = $BaseUrl.TrimEnd('/') + '/'
$paths = @('', 'styles.css','admin.css','app.js','assets/oaj-logo.png','api/meta','api/dashboard','admin/login','App_Data/sige-sise.db')
foreach($path in $paths) {
  $url = $BaseUrl + $path
  try {
    $response = Invoke-WebRequest -Uri $url -MaximumRedirection 0 -SkipHttpErrorCheck
    $status = [int]$response.StatusCode
    $expected = if($path -eq 'App_Data/sige-sise.db') { $status -in @(403,404) } else { $status -eq 200 }
    '{0}  HTTP {1}  {2}' -f $(if($expected){'OK'}else{'REVISAR'}),$status,$url
  } catch { 'ERROR  {0} - {1}' -f $url,$_.Exception.Message }
}
