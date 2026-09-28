#!/usr/bin/env python3
"""Verify command preservation and source/dependency boundaries; no game needed."""
import json, pathlib, re, subprocess
ROOT = pathlib.Path(__file__).resolve().parents[1]
def check():
    old = json.loads((ROOT / 'docs/command-inventory-before.json').read_text())
    found = {}
    for folder in ('Source', 'Packs'):
        for path in (ROOT / folder).rglob('*.cs'):
            if 'obj' in path.parts or 'bin' in path.parts: continue
            for name in re.findall(r'new Terminal.ConsoleCommand\("([^"]+)"', path.read_text()):
                assert name not in found, f'Duplicate registration: {name}'
                found[name] = str(path.relative_to(ROOT))
    assert set(old) == set(found), f'Lost/added commands: {set(old)^set(found)}'
    core = '\n'.join(p.read_text() for p in (ROOT / 'Source').rglob('*.cs'))
    for name in ('CustomCommands', 'WorldInspectionCommands', 'TerrainInspectionCommands', 'CaptureCommands', 'BuildCommands', 'AsyncCommands', 'WorldObservations', 'CallCommands', 'StaticMemberCall', 'CallValues'):
        assert not re.search(r'\b'+name+r'\b', core), f'Core depends on {name}'
    assert '<Compile Remove="Packs\\**" />' in (ROOT/'valheimCLI.csproj').read_text()
    for name in ('Standard','WorldTools','Capture','Reflection'):
        project=(ROOT/f'Packs/{name}/Valheim.Cli.{name}.csproj').read_text()
        assert '<AllowUnsafeBlocks>true</AllowUnsafeBlocks>' in project, f'{name} lost Mono publicized-member access'
        assert '<Private>false</Private>' in project
        assert project.count('ProjectReference') == 2 # opening/closing tags, core only
    for name in ('CallCommands', 'StaticMemberCall', 'CallValues'):
        for path in (ROOT/'Packs/WorldTools').glob('*.cs'):
            assert not re.search(r'\b'+name+r'\b', path.read_text()), f'World Tools depends on {name}'
    print(f'PASS: {len(old)} legacy commands preserved exactly once; core has no pack implementation references.')
    for folder in ('Source','Packs/Standard','Packs/WorldTools','Packs/Capture','Packs/Reflection'):
        names=sorted(name for name,path in found.items() if path.startswith(folder+'/'))
        print(folder, len(names))
    return found
if __name__ == '__main__': check()
