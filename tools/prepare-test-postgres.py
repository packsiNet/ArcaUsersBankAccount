"""Portable PostgreSQL for synthetic local tests only. Never use its trust auth in production."""
import pathlib, urllib.request, zipfile, tarfile, io
root = pathlib.Path('.local'); root.mkdir(exist_ok=True)
path=root/'postgres.jar'
if not path.exists():
    urllib.request.urlretrieve('https://repo1.maven.org/maven2/io/zonky/test/postgres/embedded-postgres-binaries-windows-amd64/17.6.0/embedded-postgres-binaries-windows-amd64-17.6.0.jar',path)
with zipfile.ZipFile(path) as z:
    name=next(n for n in z.namelist() if n.endswith('.txz'))
    with tarfile.open(fileobj=io.BytesIO(z.read(name)),mode='r:xz') as t: t.extractall(root/'postgres',filter='data')
print('Portable test PostgreSQL extracted under .local/postgres')
