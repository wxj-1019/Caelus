# Repo-local credential helper: strips the path field before forwarding to credential-store.
# Reason: newer git sends path=wxj-1019/Caelus.git in queries; store matches path literally
# against entry URL paths (which always carry a leading slash) and never matches.
$data = [Console]::In.ReadToEnd()
$lines = $data -split "`n"
$filtered = ($lines | Where-Object { $_ -notmatch '^path=' -and $_.Length -gt 0 }) -join "`n"
$filtered | git credential-store $args[0]
