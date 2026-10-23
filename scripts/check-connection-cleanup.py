from pathlib import Path
import concurrent.futures,json,os,socket,subprocess,time
pid=subprocess.check_output(['pgrep','-x','valheim.x86_64'],text=True).strip()
def counts():
    root=Path('/proc')/pid
    return dict(descriptors=len(list((root/'fd').iterdir())),threads=len(list((root/'task').iterdir())))
def ping(index):
    with socket.create_connection(('127.0.0.1',5555),timeout=10) as connection:
        stream=connection.makefile('rw')
        assert stream.readline().strip()=='VALHEIM_CLI_READY'
        assert stream.readline().startswith('VALHEIM_CLI_CAPS')
        stream.write('PING\n');stream.flush()
        assert stream.readline().strip()=='PONG'
        stream.close()
ping(0);time.sleep(2)
before=counts()
with concurrent.futures.ThreadPoolExecutor(10) as pool:list(pool.map(ping,range(1000)))
time.sleep(3)
after=counts();record=dict(connections=1000,before=before,after=after)
print(json.dumps(record))
assert after['descriptors']<=before['descriptors']+5,record
assert after['threads']<=before['threads']+5,record
