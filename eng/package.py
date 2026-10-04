#!/usr/bin/env python3
"""Publish self-contained desktop, plugin host and required notices. Run with Python 3.

The version comes from Directory.Build.props (see eng/version.py). ARM64 packages can be built on x64 machines.
"""
from pathlib import Path
import argparse,subprocess,shutil,urllib.request,hashlib,zipfile,tarfile,sys
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'eng'))
import version as versions
# Pinned Git for Windows builds bundled with the Windows packages (asset name, SHA-256 from the release page).
MINGIT={'win-x64':('MinGit-2.56.0-64-bit.zip','064b440ff870ed5198527e8f3a92cdf5bd2fd0fedf5e718af95e3fdaddeff718'),
        'win-arm64':('MinGit-2.56.0-arm64.zip','cb3b0f2d486ea52673227151a5baf5bc13861ff80e74e94e46d614d1bfcd5c06')}
DEB_ARCH={'linux-x64':'amd64','linux-arm64':'arm64'}
p=argparse.ArgumentParser();p.add_argument('--rid',default='linux-x64',choices=['linux-x64','linux-arm64','win-x64','win-arm64']);p.add_argument('--dotnet',default='dotnet');a=p.parse_args()
version=versions.read()
windows=a.rid.startswith('win-')
out=root/'artifacts'/('jourfold-'+a.rid)
if out.exists():shutil.rmtree(out)
out.mkdir(parents=True)
for project,folder in [('Desktop',out),('PluginHost',out/'PluginHost')]:
 subprocess.run([a.dotnet,'publish',str(root/'src'/('Jourfold.'+project)), '-c','Release','-p:NuGetLockFilePath=obj/packages.publish.lock.json','-r',a.rid,'--self-contained','true','-o',str(folder)],check=True)
subprocess.run([a.dotnet,'publish',str(root/'samples/Jourfold.ExamplePlugin'),'-c','Release','-p:NuGetLockFilePath=obj/packages.publish.lock.json','-o',str(out/'Examples/Walking')],check=True)
for file in ['LICENSE','THIRD_PARTY_NOTICES.md']:
 shutil.copy2(root/file,out/file)
if (root/'licenses').exists():shutil.copytree(root/'licenses',out/'licenses',dirs_exist_ok=True)
if windows:
 name,sha=MINGIT[a.rid]
 archive=root/'artifacts'/name
 if not archive.exists():urllib.request.urlretrieve('https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.1/'+name,archive)
 assert hashlib.sha256(archive.read_bytes()).hexdigest()==sha,'MinGit checksum mismatch'
 with zipfile.ZipFile(archive) as z:z.extractall(out/'git')
 shutil.make_archive(str(root/'artifacts'/('jourfold-'+a.rid)),'zip',out)
else:
 with tarfile.open(root/'artifacts'/('jourfold-'+a.rid+'.tar.gz'),'w:gz') as tar:tar.add(out,arcname='jourfold')
 if shutil.which('dpkg-deb'):
  arch=DEB_ARCH[a.rid]
  deb=root/'artifacts'/('deb-'+arch)
  if deb.exists():shutil.rmtree(deb)
  app=deb/'opt/jourfold';app.mkdir(parents=True);shutil.copytree(out,app,dirs_exist_ok=True)
  (deb/'DEBIAN').mkdir(exist_ok=True)
  (deb/'DEBIAN/control').write_text('Package: jourfold\nVersion: '+version+'\nArchitecture: '+arch+'\nMaintainer: Jourfold contributors\nDepends: git, libx11-6, libice6, libsm6, libfontconfig1, libglib2.0-0\nRecommends: libsecret-tools\nDescription: Local travel planning with TravelRepo\n')
  (deb/'usr/share/applications').mkdir(parents=True,exist_ok=True)
  (deb/'usr/share/applications/jourfold.desktop').write_text('[Desktop Entry]\nType=Application\nName=Jourfold\nExec=/opt/jourfold/Jourfold.Desktop %u\nIcon=jourfold\nCategories=Office;\nTerminal=false\nStartupWMClass=Jourfold\nMimeType=x-scheme-handler/jourfold;\n')
  icon=deb/'usr/share/icons/hicolor/scalable/apps';icon.mkdir(parents=True,exist_ok=True);shutil.copy2(root/'assets/branding/mark.svg',icon/'jourfold.svg')
  subprocess.run(['dpkg-deb','--root-owner-group','--build',str(deb),str(root/('artifacts/jourfold_'+version+'_'+arch+'.deb'))],check=True)
assert (out/('Jourfold.Desktop.exe' if windows else 'Jourfold.Desktop')).exists()
assert (out/'fonts/PlusJakartaSans-Regular.ttf').exists()
assert (out/'fonts/PlusJakartaSans-OFL.txt').exists()
print(out)
