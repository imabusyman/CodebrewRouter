using System;
using System.Reflection;
using Microsoft.Extensions.AI;

class Program {
    static void Main() {
        var t = typeof(IChatClient);
        foreach(var m in t.GetMethods()) {
            Console.WriteLine(m.Name);
            foreach(var p in m.GetParameters()) {
                Console.WriteLine("  " + p.Name + ": " + p.ParameterType.Name);
            }
            Console.WriteLine("  Returns: " + m.ReturnType.Name);
        }
    }
}
