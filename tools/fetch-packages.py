"""Download official NuGet packages with Python TLS when Windows Schannel is unavailable."""
import pathlib, urllib.request, zipfile, xml.etree.ElementTree as ET, re
feed = pathlib.Path('.packages'); feed.mkdir(exist_ok=True)
seen = set()
def fetch(name, version):
    key = (name.lower(), version)
    if key in seen: return
    seen.add(key)
    path = feed / f'{key[0]}.{version}.nupkg'
    if not path.exists():
        urllib.request.urlretrieve(f'https://api.nuget.org/v3-flatcontainer/{key[0]}/{version}/{key[0]}.{version}.nupkg', path)
    with zipfile.ZipFile(path) as z:
        root = ET.fromstring(z.read(next(n for n in z.namelist() if n.endswith('.nuspec'))))
    ns = {'n': root.tag.split('}')[0][1:]}
    deps = root.find('n:metadata/n:dependencies', ns)
    if deps is None: return
    groups = deps.findall('n:group', ns)
    if groups:
        eligible = [g for g in groups if not g.attrib.get('targetFramework') or 'net' in g.attrib.get('targetFramework','').lower()]
        def rank(g):
            s = g.attrib.get('targetFramework','').lower()
            m = re.search(r'net(?:coreapp)?(\d+)',s)
            return int(m[1]) if m else 0
        deps = max(eligible or groups, key=rank)
    for d in deps.findall('n:dependency', ns):
        v = re.split('[,\])]',d.attrib['version'].lstrip('[('))[0]
        if v: fetch(d.attrib['id'],v)
    print(name,version)
for n,v in [('Npgsql.EntityFrameworkCore.PostgreSQL','10.0.1'),('Microsoft.AspNetCore.Identity.EntityFrameworkCore','10.0.3'),('Microsoft.EntityFrameworkCore.Design','10.0.3'),('dotnet-ef','10.0.3')]: fetch(n,v)
