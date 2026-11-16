function move()
 print("F02_COMPLETE_START pool="..character:statPoints())
 local ok,left=character:upgradeStat("wisdom",3)
 print("F02_COMPLETE_WISDOM ok="..tostring(ok).." left="..left)
 local ok2,left2=character:upgradeStat("intelligence",1)
 print("F02_COMPLETE_INTEL ok="..tostring(ok2).." left="..left2)
 local no,left3=character:upgradeStat("wisdom",2)
 print("F02_COMPLETE_REFUSAL ok="..tostring(no).." left="..left3.." pool="..character:statPoints())
 return false
end