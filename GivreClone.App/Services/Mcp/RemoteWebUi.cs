namespace GivreClone.App.Services.Mcp;

/// <summary>Interface web mobile du contrôle à distance (PWA : « Ajouter à l'écran d'accueil »). Une seule page, sans dépendance externe ; tout texte
/// venant du jeu (noms, console) est inséré par textContent, jamais en HTML.</summary>
internal static class RemoteWebUi
{
    public const string Manifest = """
        {"name":"Givre","short_name":"Givre","start_url":"/","display":"standalone","background_color":"#14110d","theme_color":"#14110d",
         "icons":[{"src":"/icon.svg","sizes":"any","type":"image/svg+xml","purpose":"any"}]}
        """;

    public const string Icon = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><rect width="512" height="512" rx="112" fill="#14110d"/>
        <g fill="none" stroke="#e8a33d" stroke-width="30" stroke-linecap="round"><path d="M256 96v320M126 176l260 160M126 336l260-160"/></g></svg>
        """;

    public const string Html = """"
        <!doctype html>
        <html lang="fr"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
        <meta name="apple-mobile-web-app-capable" content="yes"><meta name="mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent"><meta name="theme-color" content="#14110d">
        <link rel="manifest" href="/manifest.webmanifest"><link rel="apple-touch-icon" href="/icon.svg">
        <title>Givre</title>
        <style>
        :root{--bg:#14110d;--bg2:#1d1913;--bg3:#272118;--line:#3a3124;--fg:#f0e8da;--mut:#a99d88;--acc:#e8a33d;--ok:#5fcf80;--bad:#ef6b5e;--blue:#5aa9f0}
        *{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
        html,body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.4 -apple-system,system-ui,Segoe UI,Roboto,sans-serif}
        body{padding:env(safe-area-inset-top) 0 calc(64px + env(safe-area-inset-bottom)) 0;min-height:100vh}
        header{position:sticky;top:0;z-index:5;background:var(--bg);display:flex;align-items:center;gap:8px;padding:10px 14px;border-bottom:1px solid var(--line)}
        header h1{font-size:17px;margin:0;flex:1}
        main{padding:12px 14px;max-width:760px;margin:0 auto}
        nav{position:fixed;left:0;right:0;bottom:0;display:flex;background:var(--bg2);border-top:1px solid var(--line);padding-bottom:env(safe-area-inset-bottom)}
        nav button{flex:1;background:none;border:0;color:var(--mut);padding:11px 2px;font-size:12px}
        nav button.on{color:var(--acc);font-weight:600}
        .card{background:var(--bg2);border:1px solid var(--line);border-radius:14px;padding:12px 14px;margin-bottom:10px}
        .row{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.grow{flex:1;min-width:0}
        .name{font-weight:600;font-size:16px}.mut{color:var(--mut);font-size:13px}
        .chip{display:inline-block;padding:2px 9px;border-radius:99px;font-size:12px;background:var(--bg3);color:var(--mut)}
        .chip.ok{background:#16301f;color:var(--ok)}.chip.bad{background:#3a1b18;color:var(--bad)}.chip.run{background:#3a2b10;color:var(--acc)}
        .bar{height:7px;border-radius:5px;background:var(--bg3);overflow:hidden;margin:6px 0}.bar i{display:block;height:100%;background:var(--bad)}.bar.p i{background:var(--blue)}
        button.b,select,input,textarea{font:inherit;color:var(--fg);background:var(--bg3);border:1px solid var(--line);border-radius:10px;padding:9px 12px}
        button.b{cursor:pointer}button.b.pri{background:var(--acc);color:#1a1205;border-color:var(--acc);font-weight:600}button.b.dan{background:#3a1b18;border-color:#6a2a24;color:var(--bad)}
        input,select,textarea{width:100%}.f{margin:8px 0}.f label{display:block;color:var(--mut);font-size:12px;margin-bottom:3px}
        pre{margin:0;white-space:pre-wrap;word-break:break-word;font:12.5px/1.45 ui-monospace,Menlo,Consolas,monospace}
        #con{background:#0e0c09;border:1px solid var(--line);border-radius:12px;padding:10px;height:60vh;overflow:auto}
        .l-error,.l-bad{color:var(--bad)}.l-warn{color:var(--acc)}.l-ok{color:var(--ok)}
        #toast{position:fixed;left:14px;right:14px;bottom:calc(76px + env(safe-area-inset-bottom));background:var(--bg3);border:1px solid var(--line);border-radius:12px;padding:10px 14px;display:none;z-index:9}
        .login{max-width:420px;margin:12vh auto;padding:0 16px}h2{font-size:15px;margin:14px 0 8px;color:var(--mut);font-weight:600;text-transform:uppercase;letter-spacing:.04em}
        </style></head><body>
        <div id="app"></div><div id="toast"></div>
        <script>
        'use strict';
        const $=(s,r=document)=>r.querySelector(s);
        function h(tag,props,...kids){const e=document.createElement(tag);for(const[k,v]of Object.entries(props||{})){if(k==='class')e.className=v;else if(k.startsWith('on'))e.addEventListener(k.slice(2),v);else if(v!==false&&v!=null)e.setAttribute(k,v===true?'':v)}for(const c of kids.flat())if(c!=null)e.append(c.nodeType?c:document.createTextNode(String(c)));return e}
        const store={get:k=>{try{return localStorage.getItem(k)}catch{return null}},set:(k,v)=>{try{localStorage.setItem(k,v)}catch{}}};
        let token=store.get('givre.token')||'',tab='bots',bots=[],cur=store.get('givre.bot')||'',timer=null,since=0,busy=false;
        function toast(msg,bad){const t=$('#toast');t.textContent=msg;t.style.borderColor=bad?'var(--bad)':'var(--line)';t.style.display='block';clearTimeout(toast.t);toast.t=setTimeout(()=>t.style.display='none',3800)}
        async function api(tool,args){
          const r=await fetch('/api/tool/'+tool,{method:'POST',headers:{'Authorization':'Bearer '+token,'Content-Type':'application/json'},body:JSON.stringify(args||{})});
          if(r.status===401){token='';store.set('givre.token','');render();throw new Error('Jeton refusé')}
          let j;try{j=await r.json()}catch{throw new Error('Réponse illisible')}
          if(!r.ok||j.isError)throw new Error(j.error||('Erreur '+r.status));return j}
        async function act(label,tool,args,confirmMsg){if(confirmMsg&&!confirm(confirmMsg))return;try{const j=await api(tool,args);toast(label+(j.ok===false?' : refusé ('+(j.reason||'?')+')':' : ok'),j.ok===false);refreshBots()}catch(e){toast(e.message,true)}}
        function login(){
          const inp=h('input',{type:'password',placeholder:'Jeton affiché dans Givre > Réglages',autocomplete:'off'});
          $('#app').replaceChildren(h('div',{class:'login'},h('h1',{},'Givre'),h('p',{class:'mut'},'Contrôle à distance. Entre le jeton affiché dans les réglages de Givre sur ton PC.'),
            h('div',{class:'f'},inp),h('button',{class:'b pri',onclick:async()=>{token=inp.value.trim();try{await api('get_app_status');store.set('givre.token',token);render()}catch(e){token='';toast(e.message,true)}}},'Se connecter')))}
        function shell(body){const names={bots:'Bots',console:'Console',scripts:'Scripts',accounts:'Comptes',chat:'Chat',alerts:'Alertes',fights:'Combats',hunt:'Chasse',res:'Récolte',eco:'Éco'};
          return [h('header',{},h('h1',{},'Givre'),h('button',{class:'b dan',onclick:()=>act('Arrêt d\'urgence','emergency_stop',{},'Arrêter TOUS les scripts et superviseurs ?')},'Stop')),
            h('main',{id:'main'},body),h('nav',{},Object.entries(names).map(([k,v])=>h('button',{class:tab===k?'on':'',onclick:()=>{tab=k;render()}},v)))]}
        // [x,y] dans un texte = lien « Aller à » (texte inséré par textContent, jamais en HTML)
        function linkify(text){const out=[];const rx=/\[(-?\d{1,4}),\s*(-?\d{1,4})\]/g;let last=0,m;while((m=rx.exec(text))){if(m.index>last)out.push(text.slice(last,m.index));const x=Number(m[1]),y=Number(m[2]);
            out.push(h('a',{href:'#',style:'color:var(--acc);text-decoration:underline',onclick:ev=>{ev.preventDefault();goTo(x,y)}},m[0]));last=m.index+m[0].length}
          if(last<text.length)out.push(text.slice(last));return out}
        function goTo(x,y){const on=bots.find(b=>b.pid>0&&!b.scriptRunning)||bots.find(b=>b.pid>0);if(!on){toast('Aucun bot en ligne',true);return}
          act('Trajet lancé','travel_to',{bot:on.accountId||on.name,x,y,useXy:true},'Envoyer '+(on.name||on.account)+' en ['+x+','+y+'] ?')}
        function lifeBar(b){const p=b.maxLife>0?Math.max(0,Math.min(100,100*b.life/b.maxLife)):0;return h('div',{class:'bar'},h('i',{style:'width:'+p+'%'}))}
        function podsBar(b){const p=b.podsMax>0?Math.max(0,Math.min(100,100*b.pods/b.podsMax)):0;return h('div',{class:'bar p'},h('i',{style:'width:'+p+'%'}))}
        function botCard(b){const on=b.pid>0;const id=b.accountId||b.name||b.account;
          return h('div',{class:'card'},h('div',{class:'row'},h('div',{class:'grow'},h('div',{class:'name'},b.name||b.account||'?'),h('div',{class:'mut'},(b.mode||'')+(on?' · niv. '+b.level:'')+(b.coords?' · '+b.coords:''))),
            h('span',{class:'chip '+(on?(b.scriptRunning?'run':'ok'):'bad')},on?(b.inFight?'combat':(b.scriptRunning?'script':'en ligne')):'hors ligne')),
            on?[lifeBar(b),h('div',{class:'mut'},'Vie '+b.life+'/'+b.maxLife+' · Pods '+b.pods+'/'+b.podsMax+' · '+Number(b.kamas||0).toLocaleString('fr-FR')+' k'),podsBar(b),
              b.script?h('div',{class:'mut'},'Script : '+b.script):null]:null,
            h('div',{class:'row',style:'margin-top:8px'},on?[h('button',{class:'b',onclick:()=>{cur=id;store.set('givre.bot',cur);tab='console';render()}},'Console'),
              b.scriptRunning?h('button',{class:'b',onclick:()=>act('Script arrêté','stop_script',{bot:id})},'Arrêter le script'):null,
              h('button',{class:'b dan',onclick:()=>act('Déconnecté','disconnect_bot',{bot:id},'Déconnecter '+(b.name||b.account)+' ?')},'Déconnecter')]
              :h('button',{class:'b pri',onclick:()=>act('Connexion lancée','connect_bot',{bot:id})},'Connecter')))}
        async function refreshBots(){if(tab!=='bots')return;try{bots=(await api('list_bots')).bots||[];const m=$('#main');if(m&&tab==='bots')m.replaceChildren(...(bots.length?bots.map(botCard):[h('p',{class:'mut'},'Aucun compte.')]))}catch(e){if(token)toast(e.message,true)}}
        async function pollConsole(first){if(tab!=='console'||!cur)return;try{const j=await api('read_bot_console',{bot:cur,since:first?0:since,limit:first?150:200});const box=$('#con');if(!box)return;const stick=box.scrollTop+box.clientHeight>=box.scrollHeight-40;
            if(first)box.replaceChildren();for(const l of j.lines||[]){const lv=(l.level||'').toLowerCase();box.append(h('pre',{class:'l-'+lv},(l.time||'').slice(11,19)+' ',l.category?'['+l.category+'] ':'',...linkify(l.message||'')))}since=j.cursor??since;if(stick||first)box.scrollTop=box.scrollHeight}catch(e){}}
        function viewBots(){return h('div',{},h('p',{class:'mut'},'Chargement…'))}
        function viewConsole(){const sel=h('select',{onchange:e=>{cur=e.target.value;store.set('givre.bot',cur);since=0;pollConsole(true)}},h('option',{value:''},'— choisir un bot —'),...bots.map(b=>{const id=b.accountId||b.name||b.account;return h('option',{value:id,selected:id===cur},b.name||b.account)}));
          return h('div',{},h('div',{class:'f'},sel),h('div',{id:'con'}))}
        async function viewScripts(){const m=$('#main');try{const j=await api('list_saved_scripts');try{bots=(await api('list_bots')).bots||[]}catch(e){}
            const online=bots.filter(b=>b.pid>0);const botSel=h('select',{},...online.map(b=>h('option',{value:b.accountId||b.name},(b.name||b.account)+(b.scriptRunning?' · script en cours':''))));
            const running=online.filter(b=>b.scriptRunning);
            const code=h('textarea',{rows:'14',style:'font-family:ui-monospace,Consolas,monospace;font-size:12px',placeholder:'Code Lua (function move() … end)'}),name=h('input',{placeholder:'Nom du script'});
            const need=()=>{if(!botSel.value){toast('Aucun bot en ligne',true);return false}return true};
            async function openScript(n){try{const r=await api('read_saved_script',{name:n});code.value=r.code||'';name.value=n;code.scrollIntoView({behavior:'smooth',block:'center'})}catch(e){toast(e.message,true)}}
            m.replaceChildren(h('h2',{},'Lancer sur'),h('div',{class:'f'},botSel),
              running.length?h('div',{class:'card'},...running.map(b=>h('div',{class:'row'},h('div',{class:'grow'},h('div',{class:'name'},b.name||b.account),h('div',{class:'mut'},'Script : '+(b.script||'en cours'))),h('button',{class:'b dan',onclick:()=>act('Script arrêté','stop_script',{bot:b.accountId||b.name})},'Arrêter')))):h('p',{class:'mut'},'Aucun script en cours.'),
              h('h2',{},'Scripts enregistrés ('+(j.scripts||[]).length+')'),
              ...((j.scripts||[]).length?(j.scripts||[]).map(n=>h('div',{class:'card row'},h('div',{class:'grow'},n),h('button',{class:'b',onclick:()=>openScript(n)},'Voir / modifier'),h('button',{class:'b pri',onclick:()=>need()&&act('Script lancé','run_saved_script',{name:n,bot:botSel.value},'Lancer « '+n+' » sur le bot choisi ?')},'Lancer'))):[h('p',{class:'mut'},'Aucun script enregistré.')]),
              h('h2',{},'Éditeur Lua'),h('div',{class:'f'},name),h('div',{class:'f'},code),
              h('div',{class:'row'},
                h('button',{class:'b',onclick:async()=>{if(!code.value.trim()){toast('Code vide',true);return}try{const r=await api('validate_lua_script',{code:code.value});toast(r.valid?'Syntaxe valide (la disponibilité des fonctions n\'est pas vérifiée)':'Erreur : '+r.error,!r.valid)}catch(e){toast(e.message,true)}}},'Vérifier'),
                h('button',{class:'b',onclick:async()=>{if(!name.value.trim()||!code.value.trim()){toast('Nom et code requis',true);return}try{await api('save_script',{name:name.value.trim(),code:code.value,overwrite:true});toast('Enregistré');viewScripts()}catch(e){toast(e.message,true)}}},'Enregistrer'),
                h('button',{class:'b pri',onclick:()=>need()&&code.value.trim()&&act('Script lancé','run_lua_script',{code:code.value,bot:botSel.value},'Lancer ce code sur le bot choisi ?')},'Lancer ce code')))}catch(e){toast(e.message,true)}}
        let chatSince=0,chatBot='';
        async function pollChat(first){if(tab!=='chat'||!chatBot)return;try{const j=await api('read_chat',{bot:chatBot,since:first?0:chatSince,limit:first?200:100,channel:store.get('givre.chan')||'all'});const box=$('#chat');if(!box)return;const stick=box.scrollTop+box.clientHeight>=box.scrollHeight-40;
            if(first)box.replaceChildren();for(const e of j.messages||[]){box.append(h('pre',{class:e.outgoing?'l-ok':(e.channel===10?'l-warn':'')},(e.time||'').slice(11,19)+' ['+(e.label||e.channel)+'] ',(e.outgoing?'→ ':'')+(e.sender?e.sender+' : ':''),e.text||''))}chatSince=j.cursor??chatSince;if(stick||first)box.scrollTop=box.scrollHeight}catch(e){if(first)toast(e.message,true)}}
        function viewChat(){const m=$('#main');const online=bots.filter(b=>b.pid>0);
          if(!chatBot||!online.some(b=>(b.accountId||b.name)===chatBot))chatBot=online.length?(online[0].accountId||online[0].name):'';
          const sel=h('select',{onchange:e=>{chatBot=e.target.value;chatSince=0;pollChat(true)}},...online.map(b=>h('option',{value:b.accountId||b.name,selected:(b.accountId||b.name)===chatBot},b.name||b.account)));
          const chan=h('select',{onchange:e=>{store.set('givre.chan',e.target.value);chatSince=0;pollChat(true)}},...[['all','Tous les canaux'],['mp','MP seulement']].map(([v,t])=>h('option',{value:v,selected:(store.get('givre.chan')||'all')===v},t)));
          const to=h('input',{placeholder:'Pseudo du joueur (MP)'}),txt=h('input',{placeholder:'Message',maxlength:'250'});
          const send=async()=>{if(!chatBot||!to.value.trim()||!txt.value.trim()){toast('Bot, pseudo et message requis',true);return}if(!confirm('Envoyer ce MP à '+to.value.trim()+' ?'))return;try{const r=await api('send_private_message',{bot:chatBot,target:to.value.trim(),text:txt.value});toast(r.ok?'MP envoyé':'MP refusé',!r.ok);if(r.ok)txt.value=''}catch(e){toast(e.message,true)}};
          m.replaceChildren(h('div',{class:'row'},h('div',{class:'grow'},sel),h('div',{class:'grow'},chan)),h('div',{id:'chat',style:'height:55vh;overflow:auto;background:var(--bg2);border:1px solid var(--line);border-radius:12px;padding:8px;margin:8px 0;font-size:12px'}),
            h('div',{class:'row'},h('div',{class:'grow'},to),h('div',{class:'grow'},txt),h('button',{class:'b pri',onclick:send},'Envoyer')),
            h('p',{class:'mut'},'Seul le canal MP est identifié avec certitude ; les autres canaux apparaissent sous « Canal N ».'));
          chatSince=0;pollChat(true);timer=setInterval(()=>{if(!document.hidden)pollChat(false)},2500)}
        function accRow(a){const on=!!a.online;const mode=h('select',{style:'width:auto'},h('option',{value:'socket',selected:a.mode==='socket'},'Socket (sans fenêtre)'),h('option',{value:'mitm',selected:a.mode==='mitm'},'MITM (client Dofus)'));
          return h('div',{class:'card'},h('div',{class:'row'},h('div',{class:'grow'},h('div',{class:'name'},a.label||a.id),h('div',{class:'mut'},(a.character||'—')+' · '+(a.mode==='socket'?'Socket':'MITM')+(a.blocked?' · protégé':''))),
            h('span',{class:'chip '+(on?'ok':'bad')},on?'connecté':'hors ligne')),
            a.status?h('div',{class:'mut'},a.status):null,
            a.blocked?null:h('div',{class:'row',style:'margin-top:8px'},on?[h('button',{class:'b',onclick:()=>act('Reconnexion lancée','reconnect_bot',{bot:a.id},'Reconnecter '+(a.label||a.id)+' ?')},'Reconnecter'),h('button',{class:'b dan',onclick:()=>act('Déconnecté','disconnect_bot',{bot:a.id},'Déconnecter '+(a.label||a.id)+' ?')},'Déconnecter')]
              :[h('div',{class:'grow'},mode),h('button',{class:'b pri',onclick:()=>act('Connexion lancée','connect_bot',{bot:a.id,mode:mode.value})},'Connecter')]))}
        async function viewAccounts(){const m=$('#main');try{const j=await api('list_accounts');
            const srv=h('input',{placeholder:'Serveur (nom ou numéro)'}),brd=h('input',{placeholder:'Classe (ex. Cra)'}),sx=h('select',{},h('option',{value:'m'},'Masculin'),h('option',{value:'f'},'Féminin')),nm=h('input',{placeholder:'Nom (vide = aléatoire)'}),acc=h('select',{},...(j.accounts||[]).filter(a=>!a.blocked).map(a=>h('option',{value:a.id},a.label||a.id)));
            const dsrv=h('input',{placeholder:'Serveur'}),dch=h('input',{placeholder:'Nom exact du perso à supprimer'}),dan=h('input',{type:'password',placeholder:'Réponse à la question secrète'});
            m.replaceChildren(h('h2',{},'Comptes'),...(j.accounts||[]).map(accRow),
              h('h2',{},'Nouveau personnage (Socket)'),h('div',{class:'card'},h('div',{class:'f'},h('label',{},'Compte'),acc),h('div',{class:'f'},srv),h('div',{class:'f'},brd),h('div',{class:'f'},sx),h('div',{class:'f'},nm),
                h('button',{class:'b pri',onclick:()=>act('Création lancée','create_character',{bot:acc.value,server:srv.value,breed:brd.value,sex:sx.value,name:nm.value},'Créer ce personnage ?')},'Créer')),
              h('h2',{},'Supprimer un personnage (irréversible)'),h('div',{class:'card'},h('div',{class:'f'},h('label',{},'Compte'),acc.cloneNode(true)),h('div',{class:'f'},dsrv),h('div',{class:'f'},dch),h('div',{class:'f'},dan),
                h('button',{class:'b dan',onclick:ev=>{const a=ev.target.closest('.card').querySelector('select').value;const ok=prompt('Tape le nom exact du perso pour confirmer la suppression définitive :');if(ok!==dch.value||!dch.value){toast('Confirmation différente : annulé',true);return}act('Suppression lancée','delete_character',{bot:a,server:dsrv.value,character:dch.value,answer:dan.value,confirm:true});dan.value=''}},'Supprimer définitivement')))}catch(e){toast(e.message,true)}}
        async function viewRes(){const m=$('#main');const q=h('input',{placeholder:'Ressource (ex. frêne, fer, blé…)'}),list=h('div',{}),out=h('div',{});
          const on=bots.find(b=>b.pid>0);
          async function search(){try{const j=await api('find_resource_maps',{search:q.value});const rs=(j.resources||[]).slice(0,40);list.replaceChildren(...rs.map(r=>h('div',{class:'card row',onclick:()=>pick(r)},h('div',{class:'grow'},r.name),h('span',{class:'chip'},r.jobName+' · niv. '+r.level))))}catch(e){toast(e.message,true)}}
          async function pick(r){out.replaceChildren(h('p',{class:'mut'},'Recherche…'));try{const j=await api('find_resource_maps',{resource:String(r.itemId),bot:on?(on.accountId||on.name):'',limit:12});
              out.replaceChildren(h('h2',{},r.name+(j.origin?' — depuis ['+j.origin.x+','+j.origin.y+']':' — les plus riches')),h('button',{class:'b pri',style:'margin-bottom:10px',onclick:()=>act('Script créé','make_gather_script',{resource:String(r.itemId),bot:on?(on.accountId||on.name):'',save:true},'Créer le script de récolte « '+r.name+' » ?')},'Créer un script de récolte'),...(j.maps||[]).map(n=>h('div',{class:'card row'},h('div',{class:'grow'},h('div',{class:'name'},'['+n.x+','+n.y+']'),h('div',{class:'mut'},n.count+' point(s)'+(n.indoor?' · intérieur':''))),h('span',{class:'chip'},n.distance==null?'—':(n.distance===0?'ici':n.distance+' carte(s)')))))}catch(e){out.replaceChildren();toast(e.message,true)}}
          q.addEventListener('input',()=>{clearTimeout(viewRes.t);viewRes.t=setTimeout(search,250)});
          m.replaceChildren(h('h2',{},'Où récolter'),h('p',{class:'mut'},on?'Classé depuis la carte de '+(on.name||on.account)+'.':'Aucun bot en ligne : cartes les plus riches.'),h('div',{class:'f'},q),list,out);search()}
        async function viewFights(){const m=$('#main');let lostOnly=store.get('givre.lost')==='1';const out=h('div',{}),det=h('div',{});
          const chk=h('input',{type:'checkbox',style:'width:auto;margin-right:8px'});chk.checked=lostOnly;
          async function load(){try{const j=await api('list_combat_journals',{lostOnly,limit:25});out.replaceChildren(...((j.fights||[]).length?j.fights.map(f=>h('div',{class:'card',onclick:()=>open(f.id)},
              h('div',{class:'row'},h('div',{class:'grow'},h('div',{class:'name'},f.outcome+' · '+f.turns+' tour(s) · '+f.durationSec+' s'),h('div',{class:'mut'},(f.start||'').slice(11,19)+' · '+f.casts+' sort(s)'+(f.problems?' · '+f.problems+' souci(s)':''))),
                h('span',{class:'chip '+(f.outcome==='défaite'?'bad':f.outcome==='victoire'?'ok':'')},f.outcome)),(f.findings||[]).slice(0,2).map(x=>h('div',{class:'mut',style:'margin-top:4px'},x)))):[h('p',{class:'mut'},'Aucun combat enregistré depuis le lancement de Givre.')]))}catch(e){toast(e.message,true)}}
          async function open(id){det.replaceChildren(h('p',{class:'mut'},'Chargement…'));try{const j=await api('get_combat_journal',{id,maxChars:30000});det.replaceChildren(h('h2',{},'Replay'),h('div',{class:'card'},h('pre',{},j.text)));det.scrollIntoView({behavior:'smooth'})}catch(e){det.replaceChildren();toast(e.message,true)}}
          chk.addEventListener('change',()=>{lostOnly=chk.checked;store.set('givre.lost',lostOnly?'1':'0');load()});
          m.replaceChildren(h('h2',{},'Derniers combats'),h('label',{class:'mut',style:'display:block;margin-bottom:8px'},chk,'Défaites seulement'),out,det);load()}
        // ---- alertes : long-poll permanent tant que la page est ouverte (toast + vibration + pastille dans le titre) ----
        let evLast=Number(store.get('givre.evlast')||0),unseen=0,evLoop=false,evList=[];
        const icon={death:'💀',pods:'🎒',disconnected:'🔌',fightlost:'⚔️',stuck:'⛔',arch:'👑',subscription:'📅'};
        function badge(){document.title=(unseen>0?'('+unseen+') ':'')+'Givre'}
        function onEvents(list){for(const e of list){if(e.id<=evLast)continue;evLast=e.id;evList.unshift(e);toast(e.text);unseen++;try{navigator.vibrate&&navigator.vibrate([120,60,120])}catch{}
            try{if('Notification' in window&&Notification.permission==='granted')new Notification('Givre',{body:e.text})}catch{}}
          if(evList.length>60)evList.length=60;store.set('givre.evlast',String(evLast));badge();if(tab==='alerts')viewAlerts()}
        async function evPoll(){if(evLoop||!token)return;evLoop=true;
          try{const first=await api('get_events',{since:Math.max(0,evLast-0),waitSec:0});evList=(first.events||[]).slice().reverse();if(evLast===0)evLast=first.latest||0;
            while(token){try{const j=await api('get_events',{since:evLast,waitSec:25});onEvents(j.events||[])}catch(e){await new Promise(r=>setTimeout(r,5000))}}}catch(e){}evLoop=false}
        function viewAlerts(){unseen=0;badge();const m=$('#main');if(!m)return;
          const ask=('Notification' in window&&Notification.permission==='default')?h('button',{class:'b',style:'margin-bottom:10px',onclick:async()=>{try{await Notification.requestPermission();viewAlerts()}catch{}}},'Autoriser les notifications du navigateur'):null;
          m.replaceChildren(h('h2',{},'Alertes'),h('p',{class:'mut'},'Mort, pods pleins, déconnexion, combat perdu, bot figé, archimonstre, fin d\'abonnement. Pour être prévenu téléphone fermé, renseigne le webhook Discord du perso dans Givre : Discord t\'envoie la notification.'),ask,
            ...(evList.length?evList.map(e=>h('div',{class:'card'},h('div',{class:'name'},(icon[e.kind]||'•')+' '+e.who),h('div',{},e.text),h('div',{class:'mut'},(e.at||'').slice(11,19)))):[h('p',{class:'mut'},'Aucune alerte pour l\'instant.')]))}
        let huntClues=[];
        function viewHunt(){const m=$('#main');const x=h('input',{type:'number',placeholder:'X de départ',value:store.get('givre.hx')||''}),y=h('input',{type:'number',placeholder:'Y de départ',value:store.get('givre.hy')||''}),q=h('input',{placeholder:'Nom de l\'indice (filtre)'}),out=h('div',{});let dir='';
          const dirs={right:'→ Droite',bottom:'↓ Bas',left:'← Gauche',top:'↑ Haut'};const btns=Object.entries(dirs).map(([k,v])=>h('button',{class:'b',onclick:async ev=>{dir=k;btns.forEach(b=>b.classList.remove('pri'));ev.target.classList.add('pri');await load()}},v));
          async function load(){if(x.value===''||y.value===''||!dir){return}store.set('givre.hx',x.value);store.set('givre.hy',y.value);out.replaceChildren(h('p',{class:'mut'},'Recherche…'));try{const j=await api('find_treasure_clue',{x:Number(x.value),y:Number(y.value),direction:dir});huntClues=j.clues||[];show()}catch(e){out.replaceChildren();toast(e.message,true)}}
          function norm(t){return t.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/[’`´]/g,"'")}
          function show(){const f=norm(q.value.trim());const rows=huntClues.filter(c=>!f||norm(c.name).includes(f));out.replaceChildren(...(rows.length?rows.map(c=>h('div',{class:'card row',onclick:async()=>{try{const j=await api('find_treasure_clue',{x:Number(x.value),y:Number(y.value),direction:dir,clue:c.name});const n=(j.maps||[])[0];if(!n){toast('Aucune carte',true);return}if(confirm(c.name+' : ['+n.x+','+n.y+'] à '+n.distance+' carte(s).\nY aller avec le bot ?'))goTo(n.x,n.y)}catch(e){toast(e.message,true)}}},
              h('div',{class:'grow'},c.name),h('span',{class:'chip'},'['+c.nearestX+','+c.nearestY+'] · '+c.distance))):[h('p',{class:'mut'},huntClues.length?'Aucun indice ne correspond.':'Choisis une direction.')]))}
          q.addEventListener('input',show);
          m.replaceChildren(h('h2',{},'Chasse au trésor'),h('p',{class:'mut'},'Position de départ, direction demandée par le jeu, puis touche l\'indice : la carte la plus proche s\'affiche. Source DofusDB.'),
            h('div',{class:'row'},h('div',{class:'grow'},x),h('div',{class:'grow'},y)),h('div',{class:'row',style:'margin:8px 0'},...btns),h('div',{class:'f'},q),out,h('h2',{},'Automatique'),h('button',{class:'b',onclick:()=>act('Script créé','make_treasure_script',{save:true},'Créer le script de chasse au trésor automatique ?')},'Créer le script de chasse automatique'))}
        function xpForm(){const job=h('input',{placeholder:'Métier (ex. Bûcheron)',value:store.get('givre.job')||''}),from=h('input',{type:'number',placeholder:'Niveau actuel',value:store.get('givre.jf')||'1'}),to=h('input',{type:'number',placeholder:'Niveau visé',value:store.get('givre.jt')||'100'}),mode=h('select',{},h('option',{value:'cheapest'},'Moins cher par XP'),h('option',{value:'fastest'},'Plus rapide')),out=h('div',{});
          return h('div',{},h('div',{class:'f'},job),h('div',{class:'row'},h('div',{class:'grow'},from),h('div',{class:'grow'},to)),h('div',{class:'f'},mode),
            h('button',{class:'b pri',onclick:async()=>{store.set('givre.job',job.value);store.set('givre.jf',from.value);store.set('givre.jt',to.value);out.replaceChildren(h('p',{class:'mut'},'Calcul…'));
              try{const on=bots.find(b=>b.pid>0);const j=await api('plan_job_xp',{job:job.value,fromLevel:Number(from.value),toLevel:Number(to.value),mode:mode.value,bot:on?(on.accountId||on.name):''});
                out.replaceChildren(h('div',{class:'card'},h('div',{class:'name'},j.totalCrafts.toLocaleString('fr-FR')+' crafts · niveau '+j.finalLevel),h('div',{class:'mut'},(j.totalCost!=null?Number(j.totalCost).toLocaleString('fr-FR')+' k estimés':'prix incomplets')+(j.reached?'':' · objectif non atteignable'))),
                  ...(j.steps||[]).map(s=>h('div',{class:'card row'},h('div',{class:'grow'},h('div',{},s.recipe),h('div',{class:'mut'},'niv. '+s.fromLevel+' → '+s.toLevel)),h('div',{},Number(s.crafts).toLocaleString('fr-FR')+' ×'))))}catch(e){out.replaceChildren();toast(e.message,true)}}},'Calculer le plan'),out)}
        async function viewEco(){const m=$('#main');const q=h('input',{placeholder:'Objet à fabriquer (2 lettres min.)'}),out=h('div',{});
          m.replaceChildren(h('h2',{},'Craft ou acheter'),h('div',{class:'row'},h('div',{class:'grow'},q),h('button',{class:'b pri',onclick:async()=>{try{const j=await api('get_craft_advice',{query:q.value});out.replaceChildren(h('div',{class:'card'},h('div',{class:'name'},j.name||(j.found===false?'Aucun résultat':'')),
              h('div',{class:'mut'},j.recommendation||j.note||''),j.craftCost!=null?h('div',{},'Fabriquer : '+Number(j.craftCost).toLocaleString('fr-FR')+' k'):null,j.buyCost!=null?h('div',{},'Acheter : '+Number(j.buyCost).toLocaleString('fr-FR')+' k'):null))}catch(e){toast(e.message,true)}}},'Calculer')),out,
            h('h2',{},'Monter un métier'),xpForm(),
            h('h2',{},'Abonnements'),h('div',{id:'subs'}),h('h2',{},'Patrimoine'),h('div',{id:'wealth'}));
          try{const s=await api('get_subscriptions');$('#subs').replaceChildren(...(s.accounts||[]).map(a=>h('div',{class:'card row'},h('div',{class:'grow'},a.character||a.account),h('span',{class:'chip '+(a.level==='expired'||a.level==='critical'?'bad':'ok')},a.remaining))))}catch(e){}
          const on=bots.find(b=>b.pid>0);if(on)try{const w=await api('get_wealth',{bot:on.accountId||on.name});$('#wealth').replaceChildren(h('div',{class:'card'},h('div',{class:'name'},Number(w.estimatedTotal||0).toLocaleString('fr-FR')+' k'),h('div',{class:'mut'},'estimation, '+(on.name||''))))}catch(e){}}
        async function render(){clearInterval(timer);if(!token){login();return}
          const body=tab==='bots'?viewBots():tab==='console'?viewConsole():h('p',{class:'mut'},'Chargement…');$('#app').replaceChildren(...shell(body));
          if(!bots.length&&tab!=='bots')try{bots=(await api('list_bots')).bots||[]}catch(e){}
          if(tab==='bots'){await refreshBots();timer=setInterval(()=>{if(!document.hidden)refreshBots()},3000)}
          else if(tab==='console'){const m=$('#main');m.replaceChildren(viewConsole());since=0;await pollConsole(true);timer=setInterval(()=>{if(!document.hidden)pollConsole(false)},1500)}
          else if(tab==='scripts')viewScripts();else if(tab==='accounts')viewAccounts();else if(tab==='chat')viewChat();else if(tab==='alerts')viewAlerts();else if(tab==='fights')viewFights();else if(tab==='hunt')viewHunt();else if(tab==='res')viewRes();else if(tab==='eco')viewEco()}
        render();evPoll();
        </script></body></html>
        """";
}
