"""Run bounded synthetic local k6 waves with resource samples. Requires the test app/database."""
import sys, pathlib, subprocess, json, time, threading
sys.path.insert(0,str(pathlib.Path('.local/python').resolve()))
import psutil
from datetime import datetime, timezone
exe=next(pathlib.Path('.local/k6').rglob('k6.exe'))
samples=[]; stop=False
def monitor():
    while not stop:
        processes=[]
        for p in psutil.process_iter(['pid','name','exe','memory_info','cpu_times']):
            try:
                path=(p.info['exe'] or '').lower()
                if 'getaccounts' in path and ('safetunnels.account' in path or 'postgres' in path):
                    processes.append({'pid':p.pid,'name':p.info['name'],'rss_bytes':p.info['memory_info'].rss,'cpu_seconds':sum(p.info['cpu_times'][:2])})
            except (psutil.NoSuchProcess,psutil.AccessDenied):pass
        samples.append({'utc':datetime.now(timezone.utc).isoformat(),'host_cpu_percent':psutil.cpu_percent(),'host_ram_used_bytes':psutil.virtual_memory().used,'processes':processes})
        time.sleep(1)
t=threading.Thread(target=monitor,daemon=True);t.start()
results=[]
for index,(vus,burst) in enumerate([(100,False),(300,False),(600,False),(600,True)]):
    if index:
        print('Waiting 61 seconds to separate shared-IP rate-limit windows.',flush=True)
        time.sleep(61)
    print(f'Running {vus} synthetic users'+(' burst' if burst else ' one wave'),flush=True)
    args=[str(exe),'run','-e','BASE_URL=http://127.0.0.1:5080','-e','CONFIRM_SYNTHETIC_DATABASE=yes','-e',f'VUS={vus}','-e','ONCE=1']
    if burst:args+=['-e','BURST=1']
    args+=['tests/load.k6.js']
    r=subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace')
    pathlib.Path(f'test-results/k6-{vus}'+('-burst' if burst else '')+'.log').write_text(r.stdout+r.stderr,encoding='utf-8')
    summary=pathlib.Path(f'test-results/k6-{vus}'+('-burst' if burst else '')+'.json')
    results.append({'vus':vus,'burst':burst,'exit_code':r.returncode,'metrics':json.loads(summary.read_text())['metrics'] if summary.exists() else {}})
    print('Completed, exit code '+str(r.returncode),flush=True)
stop=True;t.join(timeout=2)
pathlib.Path('test-results/resources-local.json').write_text(json.dumps({'environment':'Windows local synthetic test; load generator and app share host; not target Ubuntu server','samples':samples,'runs':results},indent=2))
print(json.dumps(results),flush=True)
if any(r['exit_code']!=0 for r in results):sys.exit(1)
