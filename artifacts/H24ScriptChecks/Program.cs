using MoonSharp.Interpreter;
foreach(var who in new[]{"Minerbot-Vaincra","Shadowbot"}) {
 var s=new Script(CoreModules.Preset_SoftSandbox);
 s.DoString("printSuccess=function() end; printMessage=function() end; printWarning=function() end; printError=function() end; local clock=100000; os={time=function() return clock end}; inFight=false; isInFight=function() return inFight end; pauses={}; delay=function(ms) table.insert(pauses,ms) end; requests=0; map={sendMapRequest=function() requests=requests+1 return true end}; leaveDialog=function() end; stopScript=function() error('unexpected terminal stop') end;");
 var code=File.ReadAllText("scripts/Mineur-H24-"+who+".lua");
 s.Globals["testMinerbot"] = who == "Minerbot-Vaincra";
 s.DoString("hp=100; regenCalls=0; getLifePointsPercent=function() return hp end; regenerate=function() regenCalls=regenCalls+1 return false end; combat={setChallengeMode=function(self,mode) assert(mode==\"off\") end,setAutoSpells=function(self,v) assert(v==false) end}; fightBasic={clearSpells=function() rotation={} end,addSpell=function(self,id,target) assert(target==0); rotation[#rotation+1]=id end};");
 s.DoString(code+"""
 currentRoute="test-route"
 h24Pause()
 assert(currentRoute==nil and exitingRoute==nil)
 assert(routeCooldown["test-route"]==100300)
 assert(pauses[#pauses]==60000 and requests==1)
 inFight=true
 h24Pause()
 assert(requests==1)
 inFight=false
 onFightEnd({won=false})
 assert(recoveryPending)
 if testMinerbot then hp=50; move(); assert(regenCalls==1 and recoveryPending and requests==1); assert(#rotation==2 and rotation[1]==13528 and rotation[2]==14396); hp=100 end
 move()
 assert(not recoveryPending and requests==2)
 onFightEnd({won=true})
 assert(not recoveryPending)
 printSuccess("checks passed")
 """);
 Console.WriteLine(who+": recovery reset, route cooldown, backoff, fight guard, defeat resume, victory passed");
}

{
 var s=new Script(CoreModules.Preset_SoftSandbox);
 s.DoString("clock=1000; os={time=function() return clock end}; calls=0; starts=0; fighting=false; online=true; status='disconnected'; active=true; bots={get=function(self,id) return {online=online,status=status,inFight=fighting,inGame=status=='ingame',scriptRunning=active} end,reconnect=function() calls=calls+1 return true end,startScript=function(self,id,code) assert(string.find(code,'function move()',1,true));starts=starts+1 return true end}; delay=function() end; print=function() end; printWarning=function() end;");
 s.DoString(File.ReadAllText("scripts/Supervision-Mineurs-H24.lua")+"""
 move(); assert(calls==2) -- disconnected is online=true in the clone
 move(); assert(calls==2) -- retry throttle
 clock=1200; fighting=true; move(); assert(calls==2)
 fighting=false; status="ingame"; move(); assert(starts==0)
 active=false; move(); assert(starts==2)
 clock=1400; onBotEvent({},"moderator",{});move();assert(starts==2 and calls==2)
 """);
 Console.WriteLine("Supervisor: disconnected status, retry throttle, fight guard, active script guard, restart, moderator suspension passed");
}

{
 var s=new Script(CoreModules.Preset_SoftSandbox);
 s.DoString("printWarning=function() end;print=function() end;delay=function() end;REPLY_TEXTS={};GOOD={};BAD={};seen={};map={getNpcs=function() return {{id=-20000,npcId=2890,name='Test'}} end};npc={npc=function(self,id) seen[#seen+1]=id;return true end,question=function() return {messageId=1,replies={123}} end,hasReply=function(self,id) return id==123 end,reply=function() return false end,leaveDialog=function() end}");
 var source=File.ReadAllText("scripts/Incarnam-Quetes-Valrianess-2026.lua");
 var start=source.IndexOf("function talk(");var end=source.IndexOf("local function waitOpen()",start);
 s.DoString(source[start..end]+"assert(talk(2890,{123}));assert(talk(-20000,{123}));assert(not talk(999,{123}));assert(#seen==2)");
 start=source.IndexOf("function solveMap(");end=source.IndexOf("-- trajet qui ne fait rien",start);
 s.DoString("SKIPNAMES={};go=function() return true end;getMapId=function() return 100 end;resolved=nil;talk=function(id) resolved=id end;"+source[start..end]+"solveMap(100,function() return false end,'test',{noUse=true});assert(resolved==2890)");
 Console.WriteLine("Incarnam: model identifier, actor identifier, absent NPC guard, generic NPC model forwarding passed");
}
