param([string]$Log, [string]$Pattern, [int]$First = 8)
Select-String -LiteralPath $Log -Pattern $Pattern | Select-Object -First $First | ForEach-Object {
    $line = $_.Line
    $line.Substring(0, [Math]::Min(500, $line.Length))
}
