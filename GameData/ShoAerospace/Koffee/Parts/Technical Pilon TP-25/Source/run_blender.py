"""Capture native Blender output as UTF-8 bytes, bypassing shell transcoding."""
from pathlib import Path
import subprocess, sys
OUT=Path(__file__).resolve().parent
task=sys.argv[1] if len(sys.argv)>1 else 'build'
script={'build':'build_tp25.py','validate':'validate_tp25.py','reopen':'reopen_tp25.py','nodes':'update_tp25_nodes.py'}[task]
cmd=['C:/Program Files/Blender Foundation/Blender 4.4/blender.exe','--background','--factory-startup',
     '--python-exit-code','1','--python',str(OUT/script)]
# Log is regenerated, not an existing project asset.
with (OUT/(task+'_execution.log')).open('wb') as log:
    p=subprocess.Popen(cmd,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    for line in p.stdout:
        line.decode('utf-8',errors='strict'); log.write(line)
    code=p.wait()
print(task, 'PASS' if code==0 else 'FAIL',flush=True)
raise SystemExit(code)
