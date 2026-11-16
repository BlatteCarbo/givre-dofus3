function move()
 print("F02_LIVE_BEFORE pool="..character:statPoints())
 local ok,left=character:upgradeStat("intelligence",1)
 print("F02_LIVE_INTEL1 ok="..tostring(ok).." left="..left)
 local ok2,left2=character:upgradeStat("intelligence",1)
 print("F02_LIVE_INTEL2 ok="..tostring(ok2).." left="..left2)
 local no,left3=character:upgradeStat("wisdom",2)
 print("F02_LIVE_BUDGET ok="..tostring(no).." left="..left3.." pool="..character:statPoints())
 local bad,left4=character:upgradeStat("invalid_f02",1)
 print("F02_LIVE_INVALID ok="..tostring(bad).." left="..left4.." pool="..character:statPoints())
 return false
end