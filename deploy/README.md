# Despliegue en VPS (staging)

PostgreSQL nativo en el host + Docker Compose para `web` (Caddy + Angular), `api` y `seq`.

## 1. Preparar el VPS (Ubuntu 24.04, una vez)

```bash
# Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER

# PostgreSQL 18 desde el repo oficial PGDG
sudo apt install -y postgresql-common
sudo /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y
sudo apt install -y postgresql-18

# Usuario y base de datos
sudo -u postgres psql -c "CREATE ROLE prenota LOGIN PASSWORD 'CHANGE_ME';"
sudo -u postgres psql -c "CREATE DATABASE prenotaconme OWNER prenota;"
sudo -u postgres psql -d prenotaconme -c "CREATE EXTENSION IF NOT EXISTS btree_gist;"
```

### Acceso desde los contenedores

`/etc/postgresql/18/main/postgresql.conf`:
```
listen_addresses = '*'
```

`/etc/postgresql/18/main/pg_hba.conf` (solo redes de Docker):
```
host  prenotaconme  prenota  172.16.0.0/12  scram-sha-256
```

```bash
sudo systemctl restart postgresql
# Firewall: 5432 nunca expuesto a internet
sudo ufw allow OpenSSH && sudo ufw allow 80,443/tcp && sudo ufw allow 443/udp
sudo ufw deny 5432 && sudo ufw enable
```

> Docker publica puertos saltándose ufw, pero Postgres no está en Docker, así que `ufw deny 5432` sí lo protege.

## 2. Configurar la app

```bash
sudo mkdir -p /opt/prenotaconme && sudo chown $USER /opt/prenotaconme
# Copiar deploy/.env.example a /opt/prenotaconme/.env y rellenar
```

Si las imágenes de GHCR son privadas: `docker login ghcr.io` en el VPS con un PAT con `read:packages`.

## 3. GitHub

- Environment `staging` con secrets: `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`.
- Cada push a `main` con CI en verde despliega automáticamente (o manual con *Run workflow*).

## 4. Operación

```bash
cd /opt/prenotaconme
docker compose logs -f api
ssh -L 5341:localhost:5341 <vps>   # Seq en http://localhost:5341
```

Backups (pgBackRest) se configuran en F5-06.
