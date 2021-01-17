#!/bin/bash
#set -e

dotnet run -p ../Simbolo.Console/Simbolo.Console.csproj --raw > raw_frames.txt

dotnet publish /p:NativeLib=Shared -r osx-x64 -c release ..
export simboloPath=../Simbolo.NativeLib/bin/release/net5.0/osx-x64/publish/Simbolo.NativeLib.dylib
gcc app.c -g -ldl -o app

while read p; do
  echo "$p"
  parts=($p)
  ./app \
    $simboloPath \
    ${parts[0]} \
    ${parts[2]} \
    ${parts[1]} \
    ${parts[3]}
done < raw_frames.txt

