-- Controle toutes les 10s ; un combat actif ne doit jamais etre interrompu.
local suspended=false
function onBotEvent(bot,event,info)
    if event == "moderator" then suspended=true printWarning("Veille suspendue : moderateur") end
end
function move()
 if suspended then delay(10000) return end
 local now=os.time()
 for _,t in ipairs(targets) do
  local b=bots:get(t.name or t.id)
  if b then
   local signature=tostring(b.mapId)..":"..tostring(b.pods)..":"..tostring(b.life)
   if b.inFight or signature~=t.signature then
    t.signature=signature t.progress=now t.stage=0
   end
   t.progress=t.progress or now
   if not b.inFight and now>=t.nextTry then
    if not b.online or b.status=="disconnected" or b.status=="offline" then
     t.nextTry=now+60
     local ok,reason=bots:reconnect(t.id)
     print("Reconnexion H24 : "..t.id.." "..tostring(ok).." "..tostring(reason))
     t.progress=now
    elseif b.inGame then
     if not b.scriptRunning then
      t.nextTry=now+20
      local ok,reason=bots:startScript(t.id,t.code)
      print("Reprise H24 : "..t.id.." "..tostring(ok).." "..tostring(reason))
      t.progress=now
     elseif now-t.progress>=180 then
      if (t.stage or 0)==0 then
       local ok,reason=bots:stopScript(t.id)
       print("Blocage H24 : relance script "..t.id.." "..tostring(ok).." "..tostring(reason))
       if ok then t.stage=1 end
      else
       local ok,reason=bots:disconnect(t.id)
       print("Blocage persistant H24 : deconnexion puis reconnexion "..t.id.." "..tostring(ok).." "..tostring(reason))
       if ok then t.stage=0 end
      end
      t.nextTry=now+10 t.progress=now
     end
    end
   end
  end
 end
 delay(10000)
end
