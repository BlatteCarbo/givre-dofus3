# Installed spell descriptor inspection

Read-only inspection through the application's real `GameNameResolver`.
Prints one complete descriptor as JSON per requested spell/grade pair.
The selected content directory and spell-level bundle SHA256 hashes are printed
to stderr so each inspection can be tied to the actual installed data.
Missing records fail with exit 1 rather than substituting another grade; malformed
argument count returns 2. It does not connect an account or execute a spell.

```powershell
dotnet run --project tools/SpellCatalogInspect -c Release -- 12927 1 12927 2 12928 1 12928 3
```

The installed client and its supported catalog loader must be available.
These are normalized application descriptors, not raw asset bytes or proof
of server trigger chronology. Inspect referenced child descriptors separately;
the tool deliberately does not infer recursive execution or mark consumption.
