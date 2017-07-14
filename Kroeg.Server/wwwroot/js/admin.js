function autocomplete(begin) {
    return fetch("/admin/complete?id=" + encodeURIComponent(begin), { credentials: 'same-origin' }).then((a) => a.json());
}

function get(id) {
    return fetch("/admin/entity?id=" + encodeURIComponent(id), { credentials: 'same-origin' }).then((a) => a.json());
}

function update(id, data) {
    return fetch("/admin/entity?id=" + encodeURIComponent(id), { credentials: 'same-origin', method: 'POST', body: JSON.stringify(data) });
}

document.addEventListener("DOMContentLoaded", () => {
    const searchField = document.getElementById("search");
    const autocompleteField = document.getElementById("autocomplete");
    const entityContainer = document.getElementById("entity-container");
    const updateEntity = document.getElementById("update-entity");
    let currentItem = null;
    let currentData = null;

    document.getElementById("update-entity").addEventListener("click", (e) => {
        e.preventDefault();

        update(currentItem, currentData);
    });

    const createEditField = (obj, begin, result) => {
        while (obj.firstChild) obj.removeChild(obj.firstChild);
        const input = document.createElement("input");
        input.type = "text";
        input.value = begin;
        input.addEventListener("keypress", (e) => {
            if (e.keyCode == 13) {
                e.preventDefault();
                result(input.value);
                input.remove();
            }
        });

        obj.appendChild(input);
    };

    const renderObject = (data, writable, container = null) => {
        container = container || document.createElement("pre");
        container.innerHTML = "{<br/>";
        for (let key of Object.keys(data)) {
            const parameter = document.createElement("div");
            const keyField = document.createElement("span");
            keyField.innerText = "    " + JSON.stringify(key) + ": [";
            parameter.appendChild(keyField);

            let values = data[key];
            const wasArray = Array.isArray();
            if (!wasArray) values = [values];
            for (let i in values) {
                const value = values[i];
                if (typeof value == 'object' && value != null) {
                    const content = renderObject(value, writable);
                    const details = document.createElement("details");
                    const summary = document.createElement("summary");
                    summary.innerText = "[embedded object]";
                    details.appendChild(summary);
                    details.appendChild(content);
                    parameter.appendChild(details);
                } else if (key != "id" && key != "@context") {
                    let obj = document.createElement("span");
                    obj.appendChild(document.createTextNode(JSON.stringify(value)));
                    obj.addEventListener("dblclick", () => {
                        createEditField(obj, obj.innerText, (text) => {
                            if (wasArray) data[key][i] = JSON.parse(text);
                            else data[key] = JSON.parse(text);
                            while (container.firstChild) container.removeChild(container.firstChild);
                            renderObject(data, true, container);
                        });
                    });

                    parameter.appendChild(obj);
                } else {
                    let obj = document.createElement("span");
                    obj.appendChild(document.createTextNode(JSON.stringify(value)));
                    parameter.appendChild(obj);
                }

            }

            parameter.appendChild(document.createTextNode("], "));

            container.appendChild(parameter);
        }

        container.appendChild(document.createTextNode("\n}"));

        return container;
    };

    let showItem = (item) => get(item).then((data) => {
        currentItem = item;
        currentData = data;

        while (entityContainer.firstChild) entityContainer.removeChild(entityContainer.firstChild);
        entityContainer.appendChild(renderObject(data));
    });

    let showAutocomplete = (text) => {
        autocomplete(text).then((list) => {
            while (autocompleteField.firstChild) autocompleteField.removeChild(autocompleteField.firstChild);

            for (let item of list) {
                const li = document.createElement("li");
                li.addEventListener("click", () => showItem(item));
                li.classList.add("list-group-item");

                const small = document.createElement("small");
                small.innerText = item;
                li.appendChild(small);

                autocompleteField.appendChild(li);
            }
        });
    }

    searchField.addEventListener("keypress", (e) => {
        if (e.keyCode == 13) {
            e.preventDefault();
            showItem(searchField.value);
            return;
        }
    });

    searchField.addEventListener("keyup", (e) => {
        const previousText = searchField.value;
        setTimeout(() => {
            if (searchField.value == previousText) {
                showAutocomplete(previousText);
            }
        }, 1000);
    });
});