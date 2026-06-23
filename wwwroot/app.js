const state = {
  session: null
};

const $ = (selector) => document.querySelector(selector);

function icons() {
  if (window.lucide) window.lucide.createIcons();
}

function showAlert(message, type = "success") {
  $("#alertHost").innerHTML = `
    <div class="alert alert-${type} alert-dismissible fade show" role="alert">
      ${message}
      <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Cerrar"></button>
    </div>`;
}

async function api(url, options = {}) {
  const response = await fetch(url, {
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    credentials: "same-origin",
    ...options
  });
  const text = await response.text();
  const data = text ? tryJson(text) : null;
  if (response.status === 401) {
    state.session = null;
    showOnly("authView");
    throw new Error("Tu sesion expiro o no tienes acceso. Inicia sesion nuevamente.");
  }
  if (response.status === 403) throw new Error("No tienes permisos para realizar esta accion.");
  if (!response.ok) throw new Error(data?.message || "No se pudo completar la operacion.");
  return data;
}

function tryJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function formData(form) {
  return Object.fromEntries(new FormData(form).entries());
}

function requireDni(dni) {
  return /^\d{8}$/.test(dni);
}

function setSession(session) {
  state.session = session;
  render();
}

async function logout() {
  await api("/api/auth/logout", { method: "POST" }).catch(() => null);
  state.session = null;
  render();
}

function showOnly(viewId) {
  ["authView", "teacherView", "studentView"].forEach(id => $("#" + id).classList.toggle("d-none", id !== viewId));
}

async function render() {
  $("#logoutBtn").classList.toggle("d-none", !state.session);
  $("#sessionName").textContent = state.session ? state.session.nombre : "";

  if (!state.session) {
    showOnly("authView");
    icons();
    return;
  }

  if (state.session.rol === "docente") {
    showOnly("teacherView");
    await loadTeacher();
  } else {
    showOnly("studentView");
    await loadStudent();
  }

  icons();
}

async function loadTeacher() {
  const filters = new URLSearchParams(cleanObject(formData($("#filtersForm"))));
  const [students, attendance, config, stats] = await Promise.all([
    api("/api/alumnos"),
    api(`/api/asistencias?${filters}`),
    api("/api/configuracion"),
    api("/api/dashboard")
  ]);

  $("#configForm").codigoActual.value = config.codigoActual;
  $("#configForm").horaInicio.value = config.horaInicio;
  $("#configForm").horaFin.value = config.horaFin;
  $("#configForm").minutosTardanza.value = config.minutosTardanza;
  renderStats(stats);
  renderStudents(students);
  renderAttendance(attendance, $("#attendanceTable"));
  updateReportLinks(filters);
}

function cleanObject(values) {
  return Object.fromEntries(Object.entries(values).filter(([, value]) => value !== ""));
}

function updateReportLinks(filters) {
  const query = filters.toString();
  $("#pdfReportLink").href = `/api/reportes/pdf${query ? `?${query}` : ""}`;
  $("#excelReportLink").href = `/api/reportes/excel${query ? `?${query}` : ""}`;
}

function renderStats(stats) {
  $("#statsGrid").innerHTML = [
    ["Presentes", stats.presentes],
    ["Tardanzas", stats.tardanzas],
    ["Faltas", stats.faltas],
    ["Alumnos", stats.totalAlumnos]
  ].map(([label, value]) => `<div class="stat"><span>${label}</span><strong>${value}</strong></div>`).join("");
}

function renderStudents(students) {
  $("#studentsTable").innerHTML = students.map(student => `
    <tr>
      <td>${student.apellidos}</td>
      <td>${student.nombres}</td>
      <td>${student.dni}</td>
      <td>${student.usuario}</td>
      <td class="text-end">
        <button class="btn btn-sm btn-outline-primary" type="button" data-edit='${JSON.stringify(student)}'>
          <i data-lucide="pencil"></i>
        </button>
        <button class="btn btn-sm btn-outline-danger" type="button" data-delete="${student.id}">
          <i data-lucide="trash-2"></i>
        </button>
      </td>
    </tr>`).join("");
  icons();
}

function renderAttendance(rows, target) {
  target.innerHTML = rows.map(row => `
    <tr>
      <td>${row.fecha}</td>
      <td>${row.horaRegistro}</td>
      <td>${row.alumno ?? ""}</td>
      <td>${row.codigo}</td>
      <td><span class="badge-state badge-${row.estado}">${row.estado}</span></td>
    </tr>`).join("") || `<tr><td colspan="5" class="text-secondary">Sin registros.</td></tr>`;
}

async function loadStudent() {
  const rows = await api(`/api/alumnos/${state.session.id}/asistencias`);
  $("#studentAttendanceTable").innerHTML = rows.map(row => `
    <tr>
      <td>${row.fecha}</td>
      <td>${row.horaRegistro}</td>
      <td>${row.codigo}</td>
      <td><span class="badge-state badge-${row.estado}">${row.estado}</span></td>
    </tr>`).join("") || `<tr><td colspan="4" class="text-secondary">Sin registros.</td></tr>`;
}

$("#loginForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const payload = formData(event.currentTarget);
    const session = await api("/api/auth/login", { method: "POST", body: JSON.stringify(payload) });
    setSession(session);
    showAlert("Sesion iniciada.");
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#studentRegisterForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const payload = formData(event.currentTarget);
    if (!requireDni(payload.dni)) throw new Error("El DNI debe tener 8 digitos.");
    await api("/api/alumnos/registro", { method: "POST", body: JSON.stringify(payload) });
    event.currentTarget.reset();
    showAlert("Alumno registrado. Ya puedes iniciar sesion.");
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#studentRegisterForm").dni.addEventListener("blur", async (event) => {
  const dni = event.currentTarget.value.trim();
  if (dni.length !== 8) return;
  const result = await api(`/api/alumnos/validar-dni/${dni}`);
  if (!result.disponible) showAlert(result.message, "warning");
});

$("#configForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const payload = formData(event.currentTarget);
    payload.minutosTardanza = Number(payload.minutosTardanza);
    await api("/api/configuracion", { method: "PUT", body: JSON.stringify(payload) });
    showAlert("Configuracion guardada.");
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#filtersForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    await loadTeacher();
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#teacherStudentForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const payload = formData(event.currentTarget);
    if (!requireDni(payload.dni)) throw new Error("El DNI debe tener 8 digitos.");
    const id = payload.id;
    delete payload.id;
    if (!payload.contrasena) delete payload.contrasena;

    await api(id ? `/api/alumnos/${id}` : "/api/alumnos", {
      method: id ? "PUT" : "POST",
      body: JSON.stringify(payload)
    });
    event.currentTarget.reset();
    showAlert("Alumno guardado.");
    await loadTeacher();
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#teacherStudentForm").dni.addEventListener("blur", async (event) => {
  const dni = event.currentTarget.value.trim();
  if (dni.length !== 8) return;
  const result = await api(`/api/alumnos/validar-dni/${dni}`);
  if (!result.disponible) showAlert(result.message, "warning");
});

$("#studentsTable").addEventListener("click", async (event) => {
  const edit = event.target.closest("[data-edit]");
  const del = event.target.closest("[data-delete]");
  if (edit) {
    const student = JSON.parse(edit.dataset.edit);
    const form = $("#teacherStudentForm");
    form.elements.id.value = student.id;
    form.elements.nombres.value = student.nombres;
    form.elements.apellidos.value = student.apellidos;
    form.elements.dni.value = student.dni;
    form.elements.usuario.value = student.usuario;
    form.elements.contrasena.value = "";
  }
  if (del && confirm("Eliminar alumno y sus asistencias?")) {
    await api(`/api/alumnos/${del.dataset.delete}`, { method: "DELETE" });
    showAlert("Alumno eliminado.");
    await loadTeacher();
  }
});

$("#clearStudentForm").addEventListener("click", () => {
  const form = $("#teacherStudentForm");
  if (form) {
    form.reset();
  }
});

$("#dailyReportBtn").addEventListener("click", async () => {
  const today = new Date().toISOString().slice(0, 10);
  $("#filtersForm").fechaInicio.value = today;
  $("#filtersForm").fechaFin.value = today;
  $("#filtersForm").alumno.value = "";
  $("#filtersForm").dni.value = "";
  $("#filtersForm").estado.value = "";
  try {
    const rows = await api(`/api/asistencias/diario?fecha=${today}`);
    renderAttendance(rows, $("#attendanceTable"));
    updateReportLinks(new URLSearchParams({ fechaInicio: today, fechaFin: today }));
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#importForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const result = await api("/api/alumnos/importar", {
      method: "POST",
      body: JSON.stringify(formData(event.currentTarget))
    });
    event.currentTarget.reset();
    const detail = result.errores.length ? ` Errores: ${result.errores.join(" | ")}` : "";
    showAlert(`Importados: ${result.importados}.${detail}`, result.errores.length ? "warning" : "success");
    await loadTeacher();
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#attendanceForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const payload = formData(event.currentTarget);
    await api("/api/asistencias/registrar", { method: "POST", body: JSON.stringify(payload) });
    event.currentTarget.reset();
    showAlert("Asistencia registrada.");
    await loadStudent();
  } catch (error) {
    showAlert(error.message, "danger");
  }
});

$("#logoutBtn").addEventListener("click", logout);

async function boot() {
  try {
    state.session = await api("/api/auth/sesion");
  } catch {
    state.session = null;
  }
  await render();
}

boot();
