(() => {
    const useNativeSelect =
        window.matchMedia(
            "(hover: none) and (pointer: coarse)"
        ).matches;

    if (useNativeSelect) {
        return;
    }

    const normalize = (value) =>
        value
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLocaleLowerCase("cs-CZ");

    const selects =
        document.querySelectorAll("[data-searchable-select-source]");

    for (const select of selects) {
        if (!(select instanceof HTMLSelectElement)) {
            continue;
        }

        const field = select.closest("label");

        if (!(field instanceof HTMLLabelElement)) {
            continue;
        }

        const customers = Array.from(select.options)
            .filter((option) => option.value.length > 0)
            .map((option) => ({
                value: option.value,
                text: option.textContent ?? "",
                searchText: normalize(option.textContent ?? "")
            }));

        const control = document.createElement("span");
        control.className = "searchable-select";

        select.before(control);
        control.append(select);

        const input = document.createElement("input");
        input.type = "search";
        input.autocomplete = "off";
        input.placeholder = "Jm\u00e9no nebo e-mail";
        input.className = "searchable-select-input";
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-expanded", "false");

        const results = document.createElement("div");
        const resultsId = `${select.id}-search-results`;

        results.id = resultsId;
        results.className = "searchable-select-results";
        results.setAttribute("role", "listbox");
        results.hidden = true;

        input.setAttribute("aria-controls", resultsId);

        control.prepend(input);
        control.append(results);
        control.classList.add("is-enhanced");

        select.tabIndex = -1;

        const selectedCustomer =
            customers.find(
                (customer) =>
                    customer.value === select.value);

        if (selectedCustomer) {
            input.value = selectedCustomer.text;
        }

        let matches = [];
        let activeIndex = -1;

        const closeResults = () => {
            results.hidden = true;
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
            activeIndex = -1;
        };

        const updateActiveOption = () => {
            const options =
                Array.from(
                    results.querySelectorAll(
                        "[data-searchable-select-option]"));

            options.forEach((option, index) => {
                const isActive = index === activeIndex;

                option.classList.toggle(
                    "is-active",
                    isActive);

                option.setAttribute(
                    "aria-selected",
                    isActive ? "true" : "false");
            });

            const activeOption =
                options[activeIndex];

            if (activeOption instanceof HTMLElement) {
                input.setAttribute(
                    "aria-activedescendant",
                    activeOption.id);

                activeOption.scrollIntoView({
                    block: "nearest"
                });
            }
            else {
                input.removeAttribute(
                    "aria-activedescendant");
            }
        };

        const chooseCustomer = (customer) => {
            select.value = customer.value;
            input.value = customer.text;

            select.dispatchEvent(
                new Event(
                    "change",
                    { bubbles: true }));

            closeResults();
        };

        const renderResults = () => {
            const query =
                normalize(input.value.trim());

            results.replaceChildren();
            matches = [];
            activeIndex = -1;

            if (query.length === 0) {
                closeResults();
                return;
            }

            matches =
                customers.filter(
                    (customer) =>
                        customer.searchText.includes(query));

            if (matches.length === 0) {
                const empty =
                    document.createElement("span");

                empty.className =
                    "searchable-select-empty";

                empty.textContent =
                    "\u017d\u00e1dn\u00fd z\u00e1kazn\u00edk neodpov\u00edd\u00e1 hled\u00e1n\u00ed.";

                results.append(empty);
            }
            else {
                matches.forEach(
                    (customer, index) => {
                        const option =
                            document.createElement("span");

                        option.id =
                            `${resultsId}-option-${index}`;

                        option.className =
                            "searchable-select-option";

                        option.setAttribute(
                            "role",
                            "option");

                        option.setAttribute(
                            "aria-selected",
                            "false");

                        option.setAttribute(
                            "data-searchable-select-option",
                            "");

                        option.textContent =
                            customer.text;

                        option.addEventListener(
                            "pointerdown",
                            (event) => {
                                event.preventDefault();
                            });

                        option.addEventListener(
                            "click",
                            () => {
                                chooseCustomer(customer);
                            });

                        results.append(option);
                    });
            }

            results.hidden = false;
            input.setAttribute(
                "aria-expanded",
                "true");
        };

        input.addEventListener(
            "input",
            () => {
                const selected =
                    customers.find(
                        (customer) =>
                            customer.value === select.value);

                if (
                    selected &&
                    normalize(input.value) !==
                        normalize(selected.text)
                ) {
                    select.value = "";
                }

                renderResults();
            });

        input.addEventListener(
            "keydown",
            (event) => {
                if (
                    event.key === "ArrowDown" ||
                    event.key === "ArrowUp"
                ) {
                    if (matches.length === 0) {
                        renderResults();
                    }

                    if (matches.length === 0) {
                        return;
                    }

                    event.preventDefault();

                    const direction =
                        event.key === "ArrowDown"
                            ? 1
                            : -1;

                    activeIndex =
                        (
                            activeIndex +
                            direction +
                            matches.length
                        ) % matches.length;

                    updateActiveOption();
                    return;
                }

                if (
                    event.key === "Enter" &&
                    activeIndex >= 0
                ) {
                    event.preventDefault();

                    chooseCustomer(
                        matches[activeIndex]);

                    return;
                }

                if (event.key === "Escape") {
                    closeResults();
                }
            });

        input.addEventListener(
            "blur",
            () => {
                window.setTimeout(
                    closeResults,
                    0);
            });
    }
})();
