export function get(object: any, name: string): any[] {
    if (name in object) {
        if (Array.isArray(object[name])) return object[name];
        return [object[name]];
    }

    return [];
}

export function contains(object: any, name: string, value: any): boolean {
    return get(object, name).indexOf(value) != -1;
}

export function containsAny(object: any, name: string, values: any[]): boolean {
    const data = get(object, name);
    for(let value of values) if (data.indexOf(values) != -1) return true;
    return false;
}

export function set(object: any, name: string, value: any) {
    if (name in object) {
        if (Array.isArray(object[name])) object[name].push(value);
        object[name] = [object[name], value];
    }

    object[name] = value;
}

export function clear(object: any, name: string) {
    if (name in object) delete object[name];
}