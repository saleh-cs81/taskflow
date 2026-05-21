// Thin wrapper over SignalR. Connects with the JWT in the query string
// (WebSockets can't set an Authorization header).
const Realtime = {
  connections: {},

  async connect(hub) {
    if (!window.signalR) return null;
    if (this.connections[hub]) return this.connections[hub];

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/${hub}`, { accessTokenFactory: () => API.accessToken })
      .withAutomaticReconnect()
      .build();

    try { await conn.start(); this.connections[hub] = conn; return conn; }
    catch (e) { console.warn(`SignalR ${hub} failed`, e); return null; }
  }
};

window.Realtime = Realtime;
