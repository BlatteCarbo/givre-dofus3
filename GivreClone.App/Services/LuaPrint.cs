using System;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

public static class LuaPrint
{
    public static DynValue Callback(Action<string> emit) => DynValue.NewCallback((_, args) =>
    {
        var parts = new string[args.Count];
        for (int i = 0; i < args.Count; i++) parts[i] = args[i].ToPrintString();
        emit(string.Join("\t", parts));
        return DynValue.Nil;
    });
}
