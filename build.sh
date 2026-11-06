#!/bin/sh
# Builds DroneFPV.dll with the C# compiler that ships with Windows and installs it into the game.
cd "$(dirname "$0")"
G="C:/Program Files (x86)/Steam/steamapps/common/The Planet Crafter Demo"
M="$G/Planet Crafter_Data/Managed"
mkdir -p out
{
  echo "-nologo -nostdlib -target:library -optimize -nowarn:618,1701,1702"
  echo "-out:out/DroneFPV.dll"
  for d in mscorlib netstandard System System.Core Assembly-CSharp UnityEngine UnityEngine.CoreModule UnityEngine.PhysicsModule UnityEngine.UI UnityEngine.UIModule UnityEngine.TextRenderingModule UnityEngine.InputLegacyModule Unity.InputSystem Unity.TextMeshPro Unity.Netcode.Runtime UnityEngine.IMGUIModule; do
    echo "-r:\"$M/$d.dll\""
  done
  echo "-r:\"$G/BepInEx/core/BepInEx.dll\""
  echo "src\DroneFPV.cs src\DroneHud.cs"
} > out/build.rsp
/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -noconfig @out/build.rsp || exit 1
mkdir -p "$G/BepInEx/plugins/DroneFPV"
cp out/DroneFPV.dll "$G/BepInEx/plugins/DroneFPV/" || { echo "install failed: close the game first (the DLL is in use)"; exit 1; }
if [ -f assets/drone.obj ]; then cp assets/drone.obj assets/drone.mtl "$G/BepInEx/plugins/DroneFPV/"; fi
echo installed
