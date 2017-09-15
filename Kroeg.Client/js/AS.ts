export function get(object: ASObject, name: string): any[] {
    if (object == null) debugger;
    if (name in object) {
        if (Array.isArray(object[name])) return object[name];
        return [object[name]];
    }

    return [];
}

export function contains(object: ASObject, name: string, value: any): boolean {
    return get(object, name).indexOf(value) != -1;
}

export function containsAny(object: ASObject, name: string, values: any[]): boolean {
    const data = get(object, name);
    for(let value of values) if (data.indexOf(values) != -1) return true;
    return false;
}

export function set(object: ASObject, name: string, value: any) {
    if (name in object) {
        if (Array.isArray(object[name])) object[name].push(value);
        object[name] = [object[name], value];
    }

    object[name] = value;
}

export function clear(object: ASObject, name: string) {
    if (name in object) delete object[name];
}

export class ASObject {
    public id: string;
    [name: string]: any;
}