function Read-FusionPool([byte[]]$Bytes) {
  if ($Bytes.Length -lt 20 -or [Text.Encoding]::ASCII.GetString($Bytes, 0, 8) -ne 'IFCFPOOL' -or
      [BitConverter]::ToUInt16($Bytes, 8) -ne 1) { throw 'Invalid fusion pool header.' }
  $species = [int][BitConverter]::ToUInt16($Bytes, 10)
  $count = [BitConverter]::ToUInt32($Bytes, 12)
  $length = [BitConverter]::ToUInt32($Bytes, 16)
  if ($species -lt 1 -or $length -ne [Math]::Ceiling($species * $species / 8.0) -or
      $Bytes.Length -ne 20 + $length -or ($count % 2) -ne 0) { throw 'Invalid fusion pool dimensions.' }
  $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  for ($i = 0; $i -lt $species * $species; $i++) {
    if (($Bytes[20 + ($i -shr 3)] -band (1 -shl ($i -band 7))) -ne 0) {
      $body = [int][Math]::Floor($i / $species) + 1
      $head = ($i % $species) + 1
      $null = $identities.Add("B${body}H$head")
    }
  }
  if ($identities.Count -ne $count) { throw 'Fusion pool population differs from its header.' }
  return ,$identities
}

function Compare-FusionPools([byte[]]$Previous, [byte[]]$Current) {
  $oldPool = Read-FusionPool $Previous
  $newPool = Read-FusionPool $Current
  $added = @($newPool | Where-Object { -not $oldPool.Contains($_) } | Sort-Object)
  $removed = @($oldPool | Where-Object { -not $newPool.Contains($_) } | Sort-Object)
  return [ordered]@{
    previous_count = $oldPool.Count; current_count = $newPool.Count
    previous_sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Previous)).ToLowerInvariant()
    current_sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Current)).ToLowerInvariant()
    added_count = $added.Count; removed_count = $removed.Count
    added = $added; removed = $removed
  }
}

