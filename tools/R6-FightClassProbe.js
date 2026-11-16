/*
 * Passive IL2CPP class/method inventory for a user-owned Dofus 3.7.11.11 test
 * client. Invoke explicitly with: frida -p <validated PID> -l tools/R6-FightClassProbe.js
 * No network calls, gameplay writes, hooks, memory patches or method invocation.
 * Never prints method arguments or instance/game/account data.
 *
 * Output is ONLY a structural candidate inventory, not proof of an animation
 * hook's semantics or ABI. Validate each candidate separately before writing
 * CombatInstant/Collapse.
 */
'use strict';
(function () {
  const main = Process.mainModule;
  if (!main || String(main.name).toLowerCase() !== 'dofus.exe') {
    console.log('R6_PROBE_FAIL wrong process (expected Dofus.exe)');
    return;
  }
  let game;
  try { game = Process.getModuleByName('GameAssembly.dll'); }
  catch (_) {
    console.log('R6_PROBE_FAIL GameAssembly.dll absent');
    return;
  }
  function native(name, ret, args) {
    try { return new NativeFunction(game.getExportByName(name), ret, args); }
    catch (_) { console.log('R6_PROBE_MISSING ' + name); return null; }
  }
  const api = {
    domain: native('il2cpp_domain_get', 'pointer', []),
    assemblies: native('il2cpp_domain_get_assemblies', 'pointer', ['pointer', 'pointer']),
    image: native('il2cpp_assembly_get_image', 'pointer', ['pointer']),
    imageName: native('il2cpp_image_get_name', 'pointer', ['pointer']),
    classCount: native('il2cpp_image_get_class_count', 'uint', ['pointer']),
    imageClass: native('il2cpp_image_get_class', 'pointer', ['pointer', 'uint']),
    className: native('il2cpp_class_get_name', 'pointer', ['pointer']),
    classNamespace: native('il2cpp_class_get_namespace', 'pointer', ['pointer']),
    methods: native('il2cpp_class_get_methods', 'pointer', ['pointer', 'pointer']),
    methodName: native('il2cpp_method_get_name', 'pointer', ['pointer']),
    methodParamCount: native('il2cpp_method_get_param_count', 'uint', ['pointer']),
    methodParam: native('il2cpp_method_get_param', 'pointer', ['pointer', 'uint']),
    methodReturn: native('il2cpp_method_get_return_type', 'pointer', ['pointer']),
    typeName: native('il2cpp_type_get_name', 'pointer', ['pointer'])
  };
  if (Object.values(api).some(f => !f)) {
    console.log('R6_PROBE_FAIL missing IL2CPP export; no result');
    return;
  }
  function str(p) { return p.isNull() ? '?' : p.readCString(); }
  const free = native('il2cpp_free', 'void', ['pointer']);
  function typeName(t) {
    if (t.isNull()) return '?';
    let p = ptr(0);
    try {
      p = api.typeName(t);
      return str(p);
    } finally {
      if (free && !p.isNull()) free(p);
    }
  }
  const countOut = Memory.alloc(Process.pointerSize);
  countOut.writePointer(ptr(0));
  const domain = api.domain();
  if (domain.isNull()) {
    console.log('R6_PROBE_FAIL no IL2CPP domain');
    return;
  }
  const list = api.assemblies(domain, countOut);
  const count = Number(countOut.readPointer().toString());
  if (list.isNull() || count < 1 || count > 2000) {
    console.log('R6_PROBE_FAIL assemblies count invalid');
    return;
  }
  const classPattern = /(fight|combat|movement|move|animation|animator|sequence|action|runner|walk|entity|character)/i;
  const methodPattern = /(move|motion|run|play|start|end|finish|wait|delay|time|transition|stop|skip|collapse|teleport|speed|tick)/i;
  const maxClasses = 240, maxMethods = 72;
  let checked = 0, hits = 0, methods = 0, limited = false;
  for (let i = 0; i < count; i++) {
    const assembly = list.add(i * Process.pointerSize).readPointer();
    if (assembly.isNull()) continue;
    const image = api.image(assembly);
    if (image.isNull()) continue;
    const source = str(api.imageName(image));
    if (!/(Core|Ankama|Assembly-CSharp)/i.test(source)) continue;
    const n = api.classCount(image);
    if (n > 60000) continue;
    for (let k = 0; k < n; k++) {
      const klass = api.imageClass(image, k);
      if (klass.isNull()) continue;
      const full = str(api.classNamespace(klass)) + '.' + str(api.className(klass));
      if (!classPattern.test(full)) continue;
      checked++;
      if (checked > maxClasses) { limited = true; break; }
      const iter = Memory.alloc(Process.pointerSize);
      iter.writePointer(ptr(0));
      const rows = [];
      for (let j = 0; j < maxMethods; j++) {
        const m = api.methods(klass, iter);
        if (m.isNull()) break;
        const name = str(api.methodName(m));
        if (!methodPattern.test(name) && !/Core\.Services\.Fight/i.test(full)) continue;
        const pcount = api.methodParamCount(m);
        if (pcount > 12) continue;
        const params = [];
        for (let p = 0; p < pcount; p++) params.push(typeName(api.methodParam(m, p)));
        rows.push(name + '(' + params.join(',') + ')->' + typeName(api.methodReturn(m)));
      }
      if (rows.length) {
        hits++;
        methods += rows.length;
        console.log('R6_CLASS ' + source + ':' + full + ' METHODS ' + rows.length);
        rows.slice(0, 36).forEach(row => console.log('R6_METHOD ' + row));
      }
    }
    if (limited) break;
  }
  console.log('R6_PROBE_DONE candidateClasses=' + checked + ' hits=' + hits +
    ' methods=' + methods + ' truncated=' + limited);
  console.log('R6_PROBE_NOTE signatures are candidates only; semantic proof requires a test fight');
})();
