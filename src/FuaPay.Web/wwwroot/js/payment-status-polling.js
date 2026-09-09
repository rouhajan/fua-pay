(() => {
    const pollIntervalMilliseconds = 2000;
    const maximumPollAttempts = 30;
    const root =
        document.querySelector("[data-payment-status-poller]");

    if (
        !(root instanceof HTMLElement) ||
        root.dataset.paymentStatusPending !== "true"
    ) {
        return;
    }

    const statusUrl = root.dataset.paymentStatusUrl;
    const badge =
        root.querySelector("[data-payment-status-badge]");
    const updatedAt =
        root.querySelector("[data-payment-status-updated-at]");
    const message =
        root.querySelector("[data-payment-status-message]");
    const currentCredit =
        document.querySelector("[data-current-credit-balance]");

    if (
        typeof statusUrl !== "string" ||
        statusUrl.length === 0 ||
        !(badge instanceof HTMLElement) ||
        !(updatedAt instanceof HTMLElement) ||
        !(message instanceof HTMLElement)
    ) {
        return;
    }

    let attempts = 0;
    let stopped = false;
    let scheduledPoll = null;

    const stopPolling = (failureMessage) => {
        stopped = true;

        if (scheduledPoll !== null) {
            window.clearTimeout(scheduledPoll);
            scheduledPoll = null;
        }

        if (typeof failureMessage === "string") {
            message.textContent = failureMessage;
            message.classList.remove("is-success");
            message.classList.add("is-warning");
        }
    };

    const schedulePoll = () => {
        if (stopped || attempts >= maximumPollAttempts) {
            stopPolling(
                "Automatická aktualizace skončila. Stav můžete obnovit ručně.");
            return;
        }

        scheduledPoll = window.setTimeout(
            pollStatus,
            pollIntervalMilliseconds);
    };

    const applyStatus = (payload) => {
        if (
            payload === null ||
            typeof payload !== "object" ||
            typeof payload.status !== "string" ||
            typeof payload.statusLabel !== "string" ||
            typeof payload.statusCssClass !== "string" ||
            typeof payload.updatedAt !== "string" ||
            typeof payload.isPending !== "boolean" ||
            !(
                payload.availableCredit === null ||
                typeof payload.availableCredit === "string"
            )
        ) {
            return false;
        }

        badge.textContent = payload.statusLabel;
        badge.className = `status-badge ${payload.statusCssClass}`;
        updatedAt.textContent = payload.updatedAt;

        if (
            payload.availableCredit !== null &&
            currentCredit instanceof HTMLElement
        ) {
            currentCredit.textContent = payload.availableCredit;
        }

        if (!payload.isPending) {
            root.querySelectorAll("[data-payment-pending-only]")
                .forEach((element) => {
                    if (element instanceof HTMLElement) {
                        element.hidden = true;
                    }
                });

            message.textContent =
                `Stav platby se změnil na „${payload.statusLabel}“. ` +
                "Podrobnosti můžete kdykoli obnovit ručně.";
        }

        return true;
    };

    async function pollStatus() {
        if (stopped || attempts >= maximumPollAttempts) {
            stopPolling(
                "Automatická aktualizace skončila. Stav můžete obnovit ručně.");
            return;
        }

        attempts += 1;

        try {
            const response = await window.fetch(
                statusUrl,
                {
                    method: "GET",
                    credentials: "same-origin",
                    cache: "no-store",
                    redirect: "error",
                    headers: {
                        Accept: "application/json"
                    }
                });

            if (!response.ok) {
                stopPolling(
                    "Automatickou aktualizaci se nepodařilo dokončit. Stav můžete obnovit ručně.");
                return;
            }

            const payload = await response.json();

            if (!applyStatus(payload)) {
                stopPolling(
                    "Automatickou aktualizaci se nepodařilo dokončit. Stav můžete obnovit ručně.");
                return;
            }

            if (!payload.isPending) {
                stopPolling();
                return;
            }

            schedulePoll();
        }
        catch {
            stopPolling(
                "Automatickou aktualizaci se nepodařilo dokončit. Stav můžete obnovit ručně.");
        }
    }

    schedulePoll();
})();
