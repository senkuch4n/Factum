// Modo sin AVX (mongo:4.4): crea el usuario de la aplicación, igual que deploy/cloud/mongo-init/01-usuario-app.js
// pero para el shell viejo "mongo", que no tiene process.env (usa _getEnv). Solo corre la primera vez
// (datadir vacío), conectado como root. factum_app solo tiene readWrite sobre factum.
const pwd = _getEnv("FACTUM_MONGO_APP_PASSWORD");
if (!pwd) throw new Error("Falta FACTUM_MONGO_APP_PASSWORD");
db.getSiblingDB("factum").createUser({ user: "factum_app", pwd: pwd, roles: [{ role: "readWrite", db: "factum" }] });
print("Usuario factum_app creado en la base factum");
