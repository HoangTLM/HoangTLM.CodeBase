"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.HtmlParser = void 0;
const parse5 = __importStar(require("parse5"));
const fs = __importStar(require("fs"));
const path = __importStar(require("path"));
const crypto = __importStar(require("crypto"));
class HtmlParser {
    scanTemplate(templateInfo, projectId, projectDir) {
        const entities = [];
        const relations = [];
        const htmlPath = templateInfo.htmlPath;
        if (!fs.existsSync(htmlPath)) {
            return { entities, relations };
        }
        const htmlContent = fs.readFileSync(htmlPath, "utf-8");
        const relativePath = path.relative(projectDir, htmlPath).replace(/\\/g, "/");
        const fileId = this.computeHash(`${projectId}:${relativePath}`);
        // Parse template as fragment with location information enabled
        const fragment = parse5.parseFragment(htmlContent, {
            sourceCodeLocationInfo: true
        });
        const traverse = (node) => {
            if (node.nodeName && !node.nodeName.startsWith("#")) {
                const tagName = node.tagName;
                const location = node.sourceCodeLocation;
                const startLine = location ? location.startLine : 1;
                const endLine = location ? location.endLine : 1;
                // 1. Catalog the HTML element itself
                const elementId = this.computeHash(`${projectId}:html-element:${templateInfo.namespace}.${templateInfo.className}:${tagName}:${startLine}:${endLine}`);
                const elementMeta = {
                    tag: tagName,
                    component: templateInfo.className,
                    code: htmlContent.substring(location ? location.startOffset : 0, location ? location.endOffset : htmlContent.length)
                };
                entities.push({
                    id: elementId,
                    fileId: fileId,
                    name: `<${tagName}>`,
                    type: "html-element",
                    signature: `<${tagName}> element in ${templateInfo.className} template`,
                    startLine: startLine,
                    endLine: endLine,
                    metadata: JSON.stringify(elementMeta),
                    relativePath: relativePath,
                    absolutePath: htmlPath
                });
                // 2. Scan Attributes for Bindings
                if (node.attrs) {
                    for (const attr of node.attrs) {
                        const attrName = attr.name;
                        const attrValue = attr.value;
                        const isEvent = attrName.startsWith("(") && attrName.endsWith(")");
                        const isProperty = attrName.startsWith("[") && attrName.endsWith("]");
                        const isStructural = attrName.startsWith("*");
                        if (isEvent || isProperty || isStructural) {
                            const attrLocation = location?.attrs?.[attrName];
                            const attrStartLine = attrLocation ? attrLocation.startLine : startLine;
                            const attrEndLine = attrLocation ? attrLocation.endLine : endLine;
                            const bindingType = isEvent ? "event-binding" : isProperty ? "property-binding" : "directive";
                            const bindingId = this.computeHash(`${projectId}:${bindingType}:${elementId}:${attrName}`);
                            const bindingMeta = {
                                elementId: elementId,
                                attribute: attrName,
                                expression: attrValue,
                                tagName: tagName,
                                component: templateInfo.className
                            };
                            entities.push({
                                id: bindingId,
                                fileId: fileId,
                                name: `${attrName}="${attrValue}"`,
                                type: bindingType,
                                signature: `${bindingType}: ${attrName} bound to ${tagName}`,
                                startLine: attrStartLine,
                                endLine: attrEndLine,
                                metadata: JSON.stringify(bindingMeta),
                                relativePath: relativePath,
                                absolutePath: htmlPath
                            });
                            // 3. Link Events to TypeScript component methods
                            if (isEvent) {
                                // Match standard function calls: e.g. onButtonClick($event), onSubmit(), or simple variables
                                const functionMatch = attrValue.trim().match(/^([a-zA-Z0-9_$]+)\s*\(/);
                                if (functionMatch) {
                                    const methodName = functionMatch[1];
                                    const targetMethodId = this.computeHash(`${projectId}:method:${templateInfo.namespace}.${templateInfo.className}.${methodName}`);
                                    const relationId = this.computeHash(`${bindingId}:${targetMethodId}`);
                                    const relationMeta = {
                                        event: attrName.replace(/[()]/g, ""),
                                        handler: attrValue,
                                        elementTag: tagName
                                    };
                                    relations.push({
                                        id: relationId,
                                        sourceId: bindingId,
                                        targetId: targetMethodId,
                                        type: "event-binding",
                                        metadata: JSON.stringify(relationMeta)
                                    });
                                }
                            }
                        }
                    }
                }
            }
            if (node.childNodes) {
                for (const child of node.childNodes) {
                    traverse(child);
                }
            }
        };
        // Begin DOM traversal
        if (fragment.childNodes) {
            for (const child of fragment.childNodes) {
                traverse(child);
            }
        }
        return { entities, relations };
    }
    computeHash(input) {
        return crypto.createHash("sha256").update(input).digest("hex");
    }
}
exports.HtmlParser = HtmlParser;
