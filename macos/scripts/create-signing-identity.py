"""One-time local signing identity. Private key is imported into login Keychain."""
import datetime, hashlib, os, subprocess, tempfile, secrets
from pathlib import Path
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import pkcs12
from cryptography.x509.oid import NameOID, ExtendedKeyUsageOID
folder = Path.home() / 'Library/Application Support/ChatGPT assistant/Signing'
folder.mkdir(parents=True, exist_ok=True, mode=0o700)
cert_path = folder / 'certificate.pem'
if cert_path.exists():
    raise SystemExit('Identity already initialized. Refusing to replace it.')
key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, 'ChatGPT Assistant Local Signing')])
now = datetime.datetime.now(datetime.timezone.utc)
cert = (x509.CertificateBuilder().subject_name(name).issuer_name(name).public_key(key.public_key())
 .serial_number(x509.random_serial_number()).not_valid_before(now-datetime.timedelta(days=1))
 .not_valid_after(now+datetime.timedelta(days=3650))
 .add_extension(x509.BasicConstraints(ca=False,path_length=None),critical=True)
 .add_extension(x509.KeyUsage(digital_signature=True,content_commitment=False,key_encipherment=False,data_encipherment=False,key_agreement=False,key_cert_sign=False,crl_sign=False,encipher_only=False,decipher_only=False),critical=True)
 .add_extension(x509.ExtendedKeyUsage([ExtendedKeyUsageOID.CODE_SIGNING]),critical=True)
 .sign(key,hashes.SHA256()))
password = secrets.token_urlsafe(32)
encryption = serialization.PrivateFormat.PKCS12.encryption_builder().kdf_rounds(50000).key_cert_algorithm(pkcs12.PBES.PBESv1SHA1And3KeyTripleDESCBC).hmac_hash(hashes.SHA1()).build(password.encode())
with tempfile.TemporaryDirectory() as tmp:
    p = Path(tmp)/'identity.p12'
    p.write_bytes(pkcs12.serialize_key_and_certificates(b'ChatGPT Assistant Local Signing',key,cert,None,encryption))
    p.chmod(0o600)
    result = subprocess.run(['security','import',str(p),'-k',str(Path.home()/'Library/Keychains/login.keychain-db'),'-P',password,'-T','/usr/bin/codesign'],check=False)
    if result.returncode: raise SystemExit('Keychain import failed')
cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
(folder/'identity.sha1').write_text(cert.fingerprint(hashes.SHA1()).hex().upper()+'\n')
print('Local code-signing certificate created; private key imported into login Keychain.')
