# Percorsi della pipeline di Bretzel, relativi al repo: tools/blender/ -> radice.
import os
ROOT=os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),"..",".."))
BRETZEL=os.path.join(ROOT,"assets","bretzel")
def source_file(name): return os.path.join(BRETZEL,name)
def work_file(name):
    d=os.path.join(BRETZEL,"work"); os.makedirs(d,exist_ok=True); return os.path.join(d,name)
def repo_file(name): return os.path.join(ROOT,name)
