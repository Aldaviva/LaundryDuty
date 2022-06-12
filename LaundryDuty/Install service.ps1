$binaryPathName = Resolve-Path(join-path $PSScriptRoot "LaundryDuty.exe")

New-Service -Name "LaundryDuty" -DisplayName "LaundryDuty" -Description "Send PagerDuty events when the laundry machine starts and stops running." -BinaryPathName $binaryPathName.Path -DependsOn Tcpip