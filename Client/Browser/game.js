(() => {
  'use strict';
  const canvas = document.getElementById('game');
  const ctx = canvas.getContext('2d');
  const $ = id => document.getElementById(id);
  const W = 1280, H = 720, FLOOR = 530;
  const image = {};
  for (const [name, path] of Object.entries({hero:'assets/hero-attack.png', guard:'assets/hero-guard.png', boss:'assets/boss.png'})) {
    image[name] = new Image(); image[name].src = path;
  }
  image.arena = new Image(); image.arena.src = 'UnityGame/Assets/Resources/Background/arena.png';
  image.heroFrames = Array.from({length:8},(_,i)=>{const frame=new Image();frame.src=`UnityGame/Assets/Resources/Sprites/Hero/hero_${String(i).padStart(2,'0')}.png`;return frame;});
  image.bossFrames = Array.from({length:8},(_,i)=>{const frame=new Image();frame.src=`UnityGame/Assets/Resources/Sprites/Boss/boss_${String(i).padStart(2,'0')}.png`;return frame;});
  image.heroIdle = Array.from({length:4},(_,i)=>{const frame=new Image();frame.src=`UnityGame/Assets/Resources/Sprites/HeroIdle/hero_idle_${String(i).padStart(2,'0')}.png`;return frame;});
  image.heroRun = Array.from({length:6},(_,i)=>{const frame=new Image();frame.src=`UnityGame/Assets/Resources/Sprites/HeroRun/hero_run_${String(i).padStart(2,'0')}.png`;return frame;});
  image.bossFacing = Array.from({length:8},(_,i)=>{const frame=new Image();frame.src=`UnityGame/Assets/Resources/Sprites/BossFacingLeft/boss_left_${String(i).padStart(2,'0')}.png`;return frame;});
  const keys = new Set();
  let state = 'menu', elapsed = 0, last = 0, shake = 0, flash = 0, bannerTime = 0, particles = [], projectiles = [], petals = [], sound = false, audio;
  let hero, boss, attackNumber = 0;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const rand = (a, b) => a + Math.random() * (b - a);
  const idleFrame = t => {const cycle=t%2.35;return cycle<1.45?0:cycle<1.73?1:cycle<1.84?2:cycle<2.13?3:0;};
  function reset() {
    hero = {x:270,y:FLOOR,vx:0,vy:0,hp:100,stamina:100,facing:1,onGround:true,attack:0,attackCooldown:0,attackHit:false,guard:false,dodge:0,dodgeCooldown:0,invuln:0,anim:0};
    boss = {x:975,hp:100,phase:1,mode:'idle',timer:1.8,pattern:0,hurt:0,anim:0,attackDone:false};
    elapsed = 0; shake = 0; flash = 0; particles = []; projectiles = []; petals = []; attackNumber = 0;
    for (let i=0;i<35;i++) petals.push({x:rand(0,W),y:rand(0,H),s:rand(1,3),speed:rand(9,23),drift:rand(7,22)});
    updateHud(); $('menu').classList.add('hidden'); $('result').classList.add('hidden'); state='playing'; showBanner('꽃피는 해안 · 결전',1.7);
  }
  function showBanner(text, seconds=1.2) { $('banner').textContent=text; $('banner').classList.remove('hidden'); bannerTime=seconds; }
  function updateHud() {
    $('playerHpBar').style.width=hero.hp+'%'; $('bossHpBar').style.width=boss.hp+'%'; $('staminaBar').style.width=hero.stamina+'%';
    $('playerHpText').textContent=Math.ceil(hero.hp)+' / 100'; $('bossHpText').textContent=Math.ceil(boss.hp)+'%';
    $('phaseLabel').textContent=boss.phase===1?'PHASE I · 잔잔한 파도':'PHASE II · 성난 파도';
  }
  function tone(freq,duration,type='sine',volume=.05) {
    if (!sound) return;
    try { audio ||= new (window.AudioContext||window.webkitAudioContext)(); const o=audio.createOscillator(),g=audio.createGain(); o.type=type;o.frequency.setValueAtTime(freq,audio.currentTime);o.frequency.exponentialRampToValueAtTime(Math.max(40,freq*.55),audio.currentTime+duration);g.gain.setValueAtTime(volume,audio.currentTime);g.gain.exponentialRampToValueAtTime(.001,audio.currentTime+duration);o.connect(g).connect(audio.destination);o.start();o.stop(audio.currentTime+duration); } catch(_) {}
  }
  function burst(x,y,color,count=14,spread=130) {for(let i=0;i<count;i++) particles.push({x,y,vx:rand(-spread,spread),vy:rand(-spread,spread),life:rand(.25,.65),max:.65,size:rand(2,6),color});}
  function heroDamage(n) {
    if (hero.invuln>0 || hero.dodge>0 || state!=='playing') return;
    if (hero.guard && hero.stamina>8) {hero.stamina=clamp(hero.stamina-n*1.8,0,100);n=Math.ceil(n*.2);burst(hero.x+30,hero.y-90,'#ffd9a1',13);tone(320,.15,'triangle');$('tipText').textContent='방어 성공! 공격이 끝난 틈을 노리세요.';}
    else {burst(hero.x,hero.y-90,'#e8857d',17);shake=10;flash=.13;tone(120,.27,'sawtooth');}
    hero.hp=clamp(hero.hp-n,0,100);hero.invuln=.65;updateHud();if(hero.hp<=0) finish(false);
  }
  function bossDamage(n) {if(state!=='playing')return;boss.hp=clamp(boss.hp-n,0,100);boss.hurt=.18;shake=4;burst(boss.x-20,FLOOR-105,'#ffe3ae',14);tone(440,.13,'triangle');updateHud();if(boss.hp<=0) finish(true);else if(boss.hp<=50&&boss.phase===1){boss.phase=2;boss.mode='idle';boss.timer=2;projectiles=[];showBanner('PHASE II · 성난 파도',2);$('tipText').textContent='보스가 격노했습니다. 파도 공격을 조심하세요!';updateHud();}}
  function finish(won){state=won?'won':'lost';$('resultEyebrow').textContent=won?'BOSS DEFEATED':'BATTLE LOST';$('resultTitle').textContent=won?'승리!':'패배';$('resultCopy').textContent=won?'해안에 다시 평온이 찾아왔습니다.':'패턴을 살펴보고 다시 도전하세요.';$('resultTime').textContent=elapsed.toFixed(1)+'초';$('resultHp').textContent=Math.ceil(hero.hp)+'%';$('result').classList.remove('hidden');tone(won?660:110,.45,'triangle',.08);}
  function startBossAttack(){boss.pattern=(boss.pattern+1)%3;boss.mode='telegraph';boss.timer=boss.phase===1?.9:.65;boss.attackDone=false;const names=['집게 휘두르기','물결 분사','물기둥'];showBanner('⚠ '+names[boss.pattern],.65);$('tipText').textContent=boss.pattern===2?'붉은 표시에서 벗어나거나 점프하세요.':'보스의 공격을 방어하거나 회피하세요.';}
  function update(dt){
    if(state!=='playing')return;elapsed+=dt;hero.anim+=dt;boss.anim+=dt;shake=Math.max(0,shake-dt*35);flash=Math.max(0,flash-dt);boss.hurt=Math.max(0,boss.hurt-dt);hero.invuln=Math.max(0,hero.invuln-dt);
    if(bannerTime>0){bannerTime-=dt;if(bannerTime<=0)$('banner').classList.add('hidden');}
    const moving=(keys.has('d')||keys.has('arrowright')?1:0)-(keys.has('a')||keys.has('arrowleft')?1:0);
    hero.guard=keys.has('k')&&hero.dodge<=0&&hero.attack<=0&&hero.stamina>1;
    if(hero.guard)hero.stamina=clamp(hero.stamina-12*dt,0,100);else hero.stamina=clamp(hero.stamina+21*dt,0,100);
    if(moving&&!hero.guard){hero.facing=moving;hero.vx=moving*(hero.dodge>0?690:330);}else hero.vx*=Math.pow(.02,dt);
    if((keys.has(' ')||keys.has('w')||keys.has('arrowup'))&&hero.onGround&&!hero.guard){hero.vy=-620;hero.onGround=false;tone(300,.1);burst(hero.x,FLOOR-7,'#b2e3e7',7,90);}
    hero.dodgeCooldown=Math.max(0,hero.dodgeCooldown-dt);
    if(keys.has('shift')&&hero.dodgeCooldown<=0&&hero.stamina>=23&&!hero.guard){hero.dodge=.34;hero.dodgeCooldown=.9;hero.stamina-=23;hero.vx=hero.facing*690;burst(hero.x,FLOOR-40,'#b9e9ef',12);tone(550,.16,'sine');}
    hero.dodge=Math.max(0,hero.dodge-dt);hero.attackCooldown=Math.max(0,hero.attackCooldown-dt);
    if(keys.has('j')&&hero.attackCooldown<=0&&!hero.guard&&hero.dodge<=0){hero.attack=.34;hero.attackCooldown=.46;hero.attackHit=false;attackNumber++;tone(480,.17,'sawtooth');}
    if(hero.attack>0){hero.attack-=dt;if(!hero.attackHit&&hero.attack<.21&&Math.abs(hero.x-boss.x)<225&&hero.facing===Math.sign(boss.x-hero.x)&&Math.abs(hero.y-FLOOR)<95){hero.attackHit=true;bossDamage(boss.mode==='telegraph'?12:8);}}
    hero.vy+=1450*dt;hero.y+=hero.vy*dt;if(hero.y>=FLOOR){hero.y=FLOOR;hero.vy=0;hero.onGround=true;}hero.x=clamp(hero.x+hero.vx*dt,70,1100);
    if(hero.x>boss.x-100)hero.x=boss.x-100;
    boss.timer-=dt;
    if(boss.mode==='idle'&&boss.timer<=0)startBossAttack();
    else if(boss.mode==='telegraph'&&boss.timer<=0){boss.mode='attacking';boss.timer=boss.pattern===1?.9:.55;if(boss.pattern===1){for(let i=0;i<(boss.phase===2?4:3);i++)projectiles.push({x:boss.x-110+i*13,y:FLOOR-100-i*35,vx:-390-i*22,vy:-30+i*4,r:21,life:3});}else if(boss.pattern===2){projectiles.push({x:clamp(hero.x+hero.vx*.35,120,1050),y:FLOOR,kind:'pillar',timer:.42,life:1.2,r:60,hit:false});}tone(180,.23,'sawtooth');}
    else if(boss.mode==='attacking'){if(boss.pattern===0&&!boss.attackDone&&boss.timer<.35){boss.attackDone=true;if(hero.x>boss.x-310&&hero.y>FLOOR-150)heroDamage(boss.phase===2?22:17);burst(boss.x-160,FLOOR-105,'#b7e9f5',22,220);}if(boss.timer<=0){boss.mode='recover';boss.timer=boss.phase===2?.55:.8;}}
    else if(boss.mode==='recover'&&boss.timer<=0){boss.mode='idle';boss.timer=boss.phase===2?.5:.9;}
    projectiles=projectiles.filter(p=>{if(p.kind==='pillar'){p.timer-=dt;p.life-=dt;if(p.timer<=0&&!p.hit){p.hit=true;burst(p.x,FLOOR-65,'#c3edff',24,160);if(Math.abs(hero.x-p.x)<75&&hero.y>FLOOR-130)heroDamage(boss.phase===2?20:15);}return p.life>0;}p.x+=p.vx*dt;p.y+=p.vy*dt;p.life-=dt;if(Math.hypot(hero.x-p.x,hero.y-92-p.y)<p.r+40){heroDamage(boss.phase===2?13:10);return false;}return p.life>0&&p.x>-50;});
    particles=particles.filter(p=>{p.x+=p.vx*dt;p.y+=p.vy*dt;p.vy+=280*dt;p.life-=dt;return p.life>0;});
    for(const p of petals){p.x-=p.drift*dt;p.y+=p.speed*dt;if(p.y>H){p.y=-10;p.x=rand(0,W)}if(p.x<0)p.x=W;}
    updateHud();
  }
  function sprite(img,sx,sy,sw,sh,x,y,w,h,flip=false){if(!img.complete||!img.naturalWidth)return;ctx.save();if(flip){ctx.translate(x+w,0);ctx.scale(-1,1);x=0;}ctx.drawImage(img,sx,sy,sw,sh,x,y,w,h);ctx.restore();}
  function background(t){
    if(image.arena.complete&&image.arena.naturalWidth){ctx.drawImage(image.arena,0,0,W,H);for(const p of petals){ctx.fillStyle='#ffd1c8bb';ctx.save();ctx.translate(p.x,p.y);ctx.rotate(t+p.x);ctx.fillRect(0,0,p.s*2,p.s);ctx.restore();}return;}
    const sky=ctx.createLinearGradient(0,0,0,H);sky.addColorStop(0,'#263d52');sky.addColorStop(.55,'#a76d6f');sky.addColorStop(1,'#ddad93');ctx.fillStyle=sky;ctx.fillRect(0,0,W,H);
    ctx.fillStyle='#ffceb288';ctx.beginPath();ctx.arc(870,220,105,0,7);ctx.fill();
    for(let layer=0;layer<3;layer++){let off=(t*(layer+1)*4)%400;ctx.fillStyle=['#354b60','#496071','#5c6872'][layer];ctx.beginPath();ctx.moveTo(-100,H);for(let x=-100;x<W+160;x+=80){let px=x-off;ctx.lineTo(px,355+layer*47+Math.sin(x*.019+layer*2)*52+Math.sin(x*.047)*16);}ctx.lineTo(W+200,H);ctx.fill();}
    ctx.fillStyle='#263e50';for(let i=0;i<8;i++){let x=i*210-60-(t*7)%210;ctx.fillRect(x,350,10,175);ctx.beginPath();ctx.moveTo(x-45,405);ctx.lineTo(x+5,330);ctx.lineTo(x+48,405);ctx.fill();}
    const sea=ctx.createLinearGradient(0,500,0,H);sea.addColorStop(0,'#668da0');sea.addColorStop(.45,'#355e74');sea.addColorStop(1,'#102b3e');ctx.fillStyle=sea;ctx.fillRect(0,505,W,H-505);
    ctx.strokeStyle='#cfdfdd55';ctx.lineWidth=2;for(let i=0;i<20;i++){let y=515+i*10;ctx.beginPath();for(let x=0;x<W;x+=26)ctx.lineTo(x,y+Math.sin(x*.025+t*(.8+i*.03)+i)*3);ctx.stroke();}
    ctx.fillStyle='#33454a';ctx.fillRect(0,FLOOR,W,H-FLOOR);ctx.fillStyle='#6f6b65';ctx.fillRect(0,FLOOR,W,13);ctx.fillStyle='#d9c3a4';ctx.fillRect(0,FLOOR,W,3);
    for(let i=0;i<24;i++){let x=i*61-20;ctx.fillStyle=i%2?'#39434a':'#435057';ctx.fillRect(x,FLOOR+16+(i%3)*11,54,20);ctx.strokeStyle='#677478';ctx.strokeRect(x,FLOOR+16+(i%3)*11,54,20);}
    for(const p of petals){ctx.fillStyle='#ffd1c8aa';ctx.save();ctx.translate(p.x,p.y);ctx.rotate(t+p.x);ctx.fillRect(0,0,p.s*2,p.s);ctx.restore();}
    const vignette=ctx.createRadialGradient(W/2,H/2,190,W/2,H/2,750);vignette.addColorStop(0,'#0000');vignette.addColorStop(1,'#061021aa');ctx.fillStyle=vignette;ctx.fillRect(0,0,W,H);
  }
  function draw(){ctx.clearRect(0,0,W,H);ctx.save();if(shake>0)ctx.translate(rand(-shake,shake),rand(-shake/2,shake/2));background(elapsed);if(!hero||!boss){ctx.restore();return;}
    if(boss.mode==='telegraph'||boss.pattern===2&&boss.mode==='attacking'){ctx.save();ctx.globalAlpha=.35+.2*Math.sin(elapsed*18);ctx.fillStyle=boss.pattern===2?'#ee4e57':'#ff7471';if(boss.pattern===2){let p=projectiles.find(p=>p.kind==='pillar');if(p){ctx.beginPath();ctx.ellipse(p.x,FLOOR-3,75,15,0,0,7);ctx.fill();}}else{ctx.fillRect(boss.x-300,FLOOR-8,290,7);}ctx.restore();}
    ctx.fillStyle='#07121a88';ctx.beginPath();ctx.ellipse(boss.x,FLOOR+7,150,18,0,0,7);ctx.fill();ctx.beginPath();ctx.ellipse(hero.x,FLOOR+5,54,10,0,0,7);ctx.fill();
    let bossFrame=boss.mode==='attacking'?5+Math.floor(boss.anim*9)%2:boss.mode==='telegraph'?4:boss.mode==='recover'?7:Math.floor(boss.anim*2)%4;
    ctx.save();if(boss.hurt>0)ctx.globalAlpha=.55;sprite(image.bossFacing[bossFrame],0,0,512,512,boss.x-240,FLOOR-461,480,480,false);ctx.restore();
    if(boss.phase===2){ctx.strokeStyle='#ff9a9a77';ctx.lineWidth=3;ctx.beginPath();ctx.ellipse(boss.x,FLOOR-125,145,100,0,0,7);ctx.stroke();}
    let heroSource,heroWidth=512,drawWidth=270;
    if(hero.guard)heroSource=image.heroFrames[7];
    else if(hero.attack>0)heroSource=image.heroFrames[hero.attack>.23?4:hero.attack>.1?5:6];
    else if(!hero.onGround)heroSource=image.heroFrames[2];
    else if(Math.abs(hero.vx)>30){heroSource=image.heroRun[Math.floor(hero.anim*12)%6];heroWidth=768;drawWidth=405;}
    else heroSource=image.heroIdle[idleFrame(hero.anim)];
    if(hero.invuln<=0||Math.floor(elapsed*16)%2===0)sprite(heroSource,0,0,heroWidth,512,hero.x-drawWidth/2,hero.y-259,drawWidth,270,hero.facing<0);
    if(hero.attack>.08&&hero.attack<.28){ctx.save();ctx.translate(hero.x+hero.facing*90,hero.y-115);ctx.scale(hero.facing,1);ctx.strokeStyle='#ffe8c4bb';ctx.lineWidth=12;ctx.shadowColor='#e87767';ctx.shadowBlur=20;ctx.beginPath();ctx.arc(0,0,115,-1.1,.85);ctx.stroke();ctx.restore();}
    if(hero.guard){ctx.strokeStyle='#f7dca9bb';ctx.lineWidth=6;ctx.beginPath();ctx.ellipse(hero.x+hero.facing*35,hero.y-97,27,80,0,0,7);ctx.stroke();}
    for(const p of projectiles){if(p.kind==='pillar'){if(p.timer<=0){let alpha=clamp(p.life/.6,0,1);ctx.fillStyle=`rgba(164,226,255,${alpha*.72})`;ctx.beginPath();ctx.moveTo(p.x-38,FLOOR);ctx.quadraticCurveTo(p.x-50,FLOOR-160,p.x,FLOOR-240);ctx.quadraticCurveTo(p.x+55,FLOOR-150,p.x+38,FLOOR);ctx.fill();}continue;}ctx.fillStyle='#a9ddf0';ctx.shadowColor='#a4eaff';ctx.shadowBlur=22;ctx.beginPath();ctx.arc(p.x,p.y,p.r,0,7);ctx.fill();ctx.shadowBlur=0;ctx.strokeStyle='#f5ffff';ctx.lineWidth=2;ctx.stroke();}
    for(const p of particles){ctx.globalAlpha=clamp(p.life/p.max,0,1);ctx.fillStyle=p.color;ctx.beginPath();ctx.arc(p.x,p.y,p.size,0,7);ctx.fill();}ctx.globalAlpha=1;
    if(flash>0){ctx.fillStyle=`rgba(255,225,205,${flash*1.7})`;ctx.fillRect(0,0,W,H);}ctx.restore();
  }
  function loop(timestamp){let dt=Math.min((timestamp-last)/1000||0,.04);last=timestamp;update(dt);draw();requestAnimationFrame(loop);}requestAnimationFrame(loop);
  window.addEventListener('keydown',e=>{let key=e.key.toLowerCase();if([' ','arrowleft','arrowright','arrowup','shift'].includes(key))e.preventDefault();keys.add(key);if(key==='enter'&&state!=='playing')reset();});window.addEventListener('keyup',e=>keys.delete(e.key.toLowerCase()));window.addEventListener('blur',()=>keys.clear());
  for(const button of document.querySelectorAll('[data-key]')){const key=button.dataset.key;button.addEventListener('pointerdown',e=>{e.preventDefault();button.setPointerCapture(e.pointerId);keys.add(key)});for(const event of ['pointerup','pointercancel','lostpointercapture'])button.addEventListener(event,()=>keys.delete(key));}
  $('startButton').addEventListener('click',reset);$('retryButton').addEventListener('click',reset);$('soundButton').addEventListener('click',()=>{sound=!sound;$('soundLabel').textContent=sound?'ON':'OFF';if(sound)tone(550,.1);});
})();
