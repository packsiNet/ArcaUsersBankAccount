"""Read NuGet's official vulnerability feed with Python TLS and inspect restored packages."""
import urllib.request,json,gzip,pathlib,re
def get(url):
    data=urllib.request.urlopen(url).read()
    if data[:2]==b'\x1f\x8b':data=gzip.decompress(data)
    return json.loads(data)
index=get('https://api.nuget.org/v3/vulnerabilities/index.json')
feed={}
for item in index:
    for package,items in get(item['@id']).items():feed.setdefault(package,[]).extend(items)
def version(s):return tuple(int(x) for x in s.split('-')[0].split('.'))+(0,)*max(0,4-len(s.split('-')[0].split('.')))
def matches(v,s):
    if s[0] not in '[(':return version(v)==version(s)
    parts=s[1:-1].split(',')
    if len(parts)==1:return version(v)==version(parts[0])
    lo,hi=(p.strip() for p in parts);n=version(v)
    return (not lo or (n>=version(lo) if s[0]=='[' else n>version(lo))) and (not hi or (n<=version(hi) if s[-1]==']' else n<version(hi)))
assets=json.loads(pathlib.Path('SafeTunnels.Account/obj/project.assets.json').read_text(encoding='utf-8-sig'))
findings=[];count=0
for package,data in assets['libraries'].items():
    if data['type']!='package':continue
    count+=1;name,v=package.split('/')
    for a in feed.get(name.lower(),[]):
        if matches(v,a['versions']):findings.append({'package':name,'version':v,**a})
report={'source':'NuGet official vulnerability feed','packages_checked':count,'findings':findings}
pathlib.Path('test-results/nuget-audit.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
if findings:raise SystemExit(1)
