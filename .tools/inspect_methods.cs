using System;
using System.IO;
using System.Linq;
using System.Reflection;

var asm = Assembly.LoadFrom(@"F:\SteamLibrary\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed\RoR2.dll");
string[] names = {
    "EntityStates.Drone.DroneWeapon.Flamethrower",
    "EntityStates.Mage.Weapon.Flamethrower",
    "EntityStates.SolusAmalgamator.FlamethrowerCannon",
    "EntityStates.SolusAmalgamator.FlamethrowerTurret",
    "EntityStates.LemurianBruiserMonster.Flamebreath"
};
foreach (var n in names)
{
    var t = asm.GetType(n);
    Console.WriteLine($"TYPE {n}");
    if (t == null) { Console.WriteLine("  <missing>"); continue; }
    foreach (var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).OrderBy(m => m.Name))
        Console.WriteLine("  " + m);
}
