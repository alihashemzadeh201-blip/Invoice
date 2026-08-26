// wwwroot/js/pwa-register.js

if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        registerServiceWorker();
        setupPWAInstallPrompt();
        setupOfflineDetection();
    });
}

async function registerServiceWorker() {
    try {
        const registration = await navigator.serviceWorker.register('/service-worker.js', {
            scope: '/'
        });

        console.log('ServiceWorker registered:', registration);

        registration.addEventListener('updatefound', () => {
            const newWorker = registration.installing;
            newWorker.addEventListener('statechange', () => {
                if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
                    showUpdateNotification();
                }
            });
        });

        setInterval(() => {
            registration.update();
        }, 60 * 60 * 1000);

    } catch (error) {
        console.error('ServiceWorker registration failed:', error);
    }
}

function showUpdateNotification() {
    const notification = document.createElement('div');
    notification.className = 'pwa-update-notification';
    notification.innerHTML = `
        <div class="pwa-update-content">
            <i class="fas fa-sync-alt"></i>
            <span>نسخه جدید موجود است</span>
            <button onclick="updateServiceWorker()" class="btn btn-sm btn-primary">به‌روزرسانی</button>
            <button onclick="this.parentElement.parentElement.remove()" class="btn btn-sm btn-secondary">بعداً</button>
        </div>
    `;
    document.body.appendChild(notification);
}

window.updateServiceWorker = async function () {
    const registration = await navigator.serviceWorker.getRegistration();
    if (registration && registration.waiting) {
        registration.waiting.postMessage({ type: 'SKIP_WAITING' });
        window.location.reload();
    }
};

let deferredPrompt;

function setupPWAInstallPrompt() {
    window.addEventListener('beforeinstallprompt', (e) => {
        e.preventDefault();
        deferredPrompt = e;
        showInstallButton();
    });

    window.addEventListener('appinstalled', () => {
        console.log('PWA installed successfully');
        deferredPrompt = null;
        hideInstallButton();
    });
}

function showInstallButton() {
    const installBtn = document.getElementById('pwa-install-btn');
    if (installBtn) {
        installBtn.style.display = 'block';
    } else {
        const btn = document.createElement('button');
        btn.id = 'pwa-install-btn';
        btn.className = 'pwa-install-button';
        btn.innerHTML = '<i class="fas fa-download"></i> نصب اپلیکیشن';
        btn.onclick = installPWA;
        document.body.appendChild(btn);
    }
}

function hideInstallButton() {
    const installBtn = document.getElementById('pwa-install-btn');
    if (installBtn) {
        installBtn.style.display = 'none';
    }
}

async function installPWA() {
    if (!deferredPrompt) return;

    deferredPrompt.prompt();
    const { outcome } = await deferredPrompt.userChoice;
    console.log(`User response: ${outcome}`);
    deferredPrompt = null;
    hideInstallButton();
}

function setupOfflineDetection() {
    window.addEventListener('online', () => {
        updateOnlineStatus(true);
    });

    window.addEventListener('offline', () => {
        updateOnlineStatus(false);
    });

    updateOnlineStatus(navigator.onLine);
}

function updateOnlineStatus(isOnline) {
    const statusBar = getOrCreateStatusBar();

    if (isOnline) {
        statusBar.className = 'pwa-status-bar online';
        statusBar.innerHTML = '<i class="fas fa-wifi"></i> آنلاین';
        setTimeout(() => statusBar.remove(), 3000);
    } else {
        statusBar.className = 'pwa-status-bar offline';
        statusBar.innerHTML = '<i class="fas fa-wifi-slash"></i> آفلاین';
    }
}

function getOrCreateStatusBar() {
    let statusBar = document.getElementById('pwa-status-bar');
    if (!statusBar) {
        statusBar = document.createElement('div');
        statusBar.id = 'pwa-status-bar';
        document.body.appendChild(statusBar);
    }
    return statusBar;
}

window.PWA = {
    installPWA
};