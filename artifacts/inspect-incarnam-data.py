import sqlite3, json, sys
c = sqlite3.connect('file:%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/gamedata/dofus.sqlite?mode=ro', uri=True)
if len(sys.argv)==1:
    print(c.execute("select name from sqlite_master where type='table' and (name like '%Skill%' or name like '%Recipe%' or name like '%Item%')").fetchall())
else:
    for row in c.execute(sys.argv[1]): print(row)
