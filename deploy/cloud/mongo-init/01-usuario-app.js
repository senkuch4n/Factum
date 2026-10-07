// Crea el usuario de la aplicación (SDD despliegue-nube DT4). Solo corre la primera vez (datadir vacío):
// el entrypoint de mongo:7 lo ejecuta conectado como root. factum_app solo tiene readWrite sobre factum;
// root queda para backup, restore y administración.
const pwd = process.env.FACTUM_MONGO_APP_PASSWORD;
if (!pwd) throw new Error("Falta FACTUM_MONGO_APP_PASSWORD");
db.getSiblingDB("factum").createUser({ user: "factum_app", pwd, roles: [{ role: "readWrite", db: "factum" }] });
print("Usuario factum_app creado en la base factum");
