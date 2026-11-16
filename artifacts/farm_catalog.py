import sqlite3
c = sqlite3.connect('file:%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/gamedata/dofus.sqlite?mode=ro', uri=True)
print('map tables', c.execute("select name from sqlite_master where type='table' and name like '%Map%'").fetchall()[:25])
print('tainela', c.execute('select target_id from SubAreaData_mapIds_junction where SubAreaData_id=30').fetchall())
