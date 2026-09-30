# Percorsi della pipeline, relativi al repo: tools/blender/ -> radice.
import os
ROOT=os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),"..",".."))
# which pet the pipeline works on (PET=sally blender ... ), Zaira by default
ZAIRA=os.path.join(ROOT,"assets",os.environ.get("PET","zaira"))
def source_file(name): return os.path.join(ZAIRA,name)
def work_file(name):
    d=os.path.join(ZAIRA,"work"); os.makedirs(d,exist_ok=True); return os.path.join(d,name)
def repo_file(name): return os.path.join(ROOT,name)
