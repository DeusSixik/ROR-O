using System;
using System.Linq;
using System.Reflection;

var asm = Assembly.LoadFrom(@"F:\SteamLibrary\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed\com.unity.multiplayer-hlapi.Runtime.dll");
var type = asm.GetType("UnityEngine.Networking.NetworkProximityChecker");
Console.WriteLine(type);
foreach (var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
    Console.WriteLine($"FIELD {field.Attributes} {field.FieldType.Name} {field.Name}");
foreach (var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
    if (method.Name=="Update" || method.Name=="OnRebuildObservers") Console.WriteLine($"METHOD {method}");
