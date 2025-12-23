//Para no usar nav link click
window.sidebarHelper = {
    closeSidebarOnMobile: function () {
        if (window.innerWidth <= 768 && document.querySelector('.sidebar').classList.contains('open')) {
            document.querySelector('.sidebar').classList.remove('open');
            document.querySelector('.main-content').classList.remove('expanded');
        }
    }
};

window.mostrarAlertaConCallback = function (mensaje, tipo, dotNetRef) {

    $(document).ready(function () {
        Swal.fire({
            icon: tipo,
            title: tipo === "success" ? "Éxito" : "Error",
            html: mensaje,
            confirmButtonText: "OK"
        }).then(function () {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("AlertaFinalizada");
            }
        });
    });
};




window.showAlert = function (icon, title, message) {
    setTimeout(function () {
        Swal.fire({
            icon: icon,
            title: title,
            text: message,
            confirmButtonText: "OK"


        });
    }, 100); // retraso leve para asegurar que el DOM esté listo
};


window.mostrarAlerta = function (tipo, titulo, mensaje) {
    Swal.fire({
        icon: tipo,
        title: titulo,
        text: mensaje
    });
};