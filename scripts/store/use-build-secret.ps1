<#
.SYNOPSIS
Puts the app's one build-time secret into this step's environment, under the name its build reads.
.DESCRIPTION
The Store workflow passes the optional BUILD_SECRET secret and the build_secret_env_name input as
$env:BUILD_SECRET and $env:BUILD_SECRET_ENV_NAME. With both set, the secret is copied to a process
environment variable of that name (MSBuild reads it as a property of the same name). With neither,
nothing is set and the build runs as before. One without the other is a misconfiguration and stops the
run, so a build never ships silently without the secret it expects. The value is never printed.
#>
$ErrorActionPreference = 'Stop'

$name = $env:BUILD_SECRET_ENV_NAME
$value = $env:BUILD_SECRET

if (-not $name -and -not $value) {
    return
}
if (-not $name) {
    throw 'BUILD_SECRET was passed but build_secret_env_name is empty; name the variable the build reads.'
}
if (-not $value) {
    throw "build_secret_env_name is '$name' but no BUILD_SECRET was passed; pass it in the caller's secrets."
}
if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw "build_secret_env_name '$name' is not a valid environment variable name."
}
if ($null -ne [Environment]::GetEnvironmentVariable($name)) {
    throw "build_secret_env_name '$name' is already set in this environment; choose a name of the app's own."
}

[Environment]::SetEnvironmentVariable($name, $value, 'Process')
Write-Output "Build secret set as $name."
