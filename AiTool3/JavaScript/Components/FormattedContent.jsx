﻿// FormattedContent.jsx

function fixNewlinesInStrings(jsonString) {
    return jsonString.replace(
        /("find"|"replace")\s*:\s*"((?:\\.|[^"\\])*?)"/g,
        (match, key, value) => {
            const fixedValue = value.replace(/\n/g, '\\n');
            return `${key}: "${fixedValue}"`;
        }
    );
}

function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

const FormattedContent = ({ content, guid, codeBlockCounter, onCodeBlockRendered }) => {
    const { colorScheme } = React.useColorScheme();
    const [currentlySelectedFindAndReplaceSet, setCurrentlySelectedFindAndReplaceSet] = useState(window.currentlySelectedFindAndReplaceSet);
    const [selectedMessageGuid, setSelectedMessageGuid] = useState(window.selectedMessageGuid);
    const [isInstallingTheme, setIsInstallingTheme] = useState(false);

    useEffect(() => {
        const handleFindAndReplaceUpdate = () => {
            setCurrentlySelectedFindAndReplaceSet(window.currentlySelectedFindAndReplaceSet);
            setSelectedMessageGuid(window.selectedMessageGuid);
        };

        window.addEventListener('findAndReplaceUpdate', handleFindAndReplaceUpdate);
        return () => window.removeEventListener('findAndReplaceUpdate', handleFindAndReplaceUpdate);
    }, []);

    const fileTypes = {
        webView: ["html", "js"],
        viewJsonStringArray: ["json"],
        viewSvg: ["svg", "xml", "html"],
        installTheme: ["maxtheme.json"],
        browseJsonObject: ["json"],
        viewMermaidDiagram: ["mermaid"],
        viewPlantUMLDiagram: ["plantuml"],
        viewDOTDiagram: ["dot"],
        runPythonScript: ["python"],
        launchSTL: ["stl"],
        runPowerShellScript: ["powershell"],
        selectFindAndReplaceScript: ["findandreplace.json"],
        selectFindAndReplaceScript2: ["findandreplace2.json"],
    };

    const addMessageButton = (label, action, dataType) => (
        <button
            onClick={action}
            style={{
                background: colorScheme.buttonBackgroundCss ? colorScheme.buttonBackgroundCss : 'none',
                backgroundColor: colorScheme.buttonBackgroundColor,
                color: colorScheme.buttonTextColor,
                border: colorScheme.buttonBorder ? colorScheme.buttonBorder : 'none',
                borderRadius: colorScheme.borderRadius ? colorScheme.borderRadius : '3px',
                padding: '3px 8px',
                cursor: 'pointer',
                marginRight: '5px',
            }}
        >
            {label}
        </button>
    );

    const formatContent = (text) => {
        const codeBlockRegex = /\u0060\u0060\u0060(.*?)\n([\s\S]*?)\u0060\u0060\u0060/g;
        const parts = [];
        let lastIndex = 0;

        // Handle code blocks first
        text.replace(codeBlockRegex, (match, fileType, code, offset) => {
            if (offset > lastIndex) {
                // Process text before the code block for URLs
                const beforeCodeBlock = text.slice(lastIndex, offset);
                parts.push(...formatUrls(beforeCodeBlock));
            }

            // Add the code block as is, without URL formatting
            parts.push(
                <div key={offset}>
                    <div style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        fontWeight: 'bold',
                        backgroundColor: colorScheme.codeBlockHeaderBackgroundColor,
                        color: colorScheme.codeBlockHeaderTextColor,
                        padding: '5px 10px',
                        borderTopLeftRadius: '5px',
                        borderTopRightRadius: '5px',
                        overflowWrap: 'anywhere'
                    }}>
                        <span>{fileType.trim()}</span>
                        <div>
                            {addMessageButton("Copy", () => {
                                window.chrome.webview.postMessage({
                                    type: 'Copy',
                                    content: code.trim()
                                });
                            })}
                            {addMessageButton("Save As", () => {
                                window.chrome.webview.postMessage({
                                    type: 'Save As',
                                    dataType: fileType.trim().toLowerCase(),
                                    content: code.trim()
                                });
                            })}
                            {/* Additional buttons for different file types */}
                            {fileTypes.webView.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("WebView", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'WebView',
                                        content: code.trim()
                                    });
                                })
                            }
                            {fileTypes.viewSvg.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("View SVG", () => {
                                    createSvgViewer(code.trim());
                                })
                            }
                            {fileTypes.viewJsonStringArray.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("View JSON String Array", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'View JSON String Array',
                                        content: code.trim()
                                    });
                                })
                            }
                            {fileTypes.installTheme.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("Install Theme", debounce(() => {
                                    if (isInstallingTheme) return;
                                    setIsInstallingTheme(true);

                                    try {
                                        var obj = JSON.parse(code.trim());
                                    }
                                    catch (error) {
                                        setUserPrompt("That JSON isn't valid: " + error);
                                        setIsInstallingTheme(false);
                                        return;
                                    }
                                    var themeName = Object.keys(obj)[0];
                                    window.addColorScheme(themeName, Object.values(obj)[0]);

                                    Promise.all([
                                        window.chrome.webview.postMessage({
                                            type: 'allThemes',
                                            content: JSON.stringify(window.getAllColorSchemes())
                                        }),
                                        window.chrome.webview.postMessage({
                                            type: 'selectTheme',
                                            content: JSON.stringify(obj.colorScheme.id)
                                        })
                                    ]).then(() => {
                                        setIsInstallingTheme(false);
                                    });
                                }, 300))
                            }
                            {fileTypes.browseJsonObject.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("Browse JSON Object", () => {
                                    createJsonViewer(code.trim());
                                })
                            }
                            {fileTypes.viewMermaidDiagram.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("View Mermaid Diagram", () => {
                                    createMermaidViewer(code.trim());
                                })
                            }
                            {fileTypes.viewPlantUMLDiagram.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("View PlantUML Diagram", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'View PlantUML Diagram',
                                        content: code.trim()
                                    });
                                })
                            }
                            {fileTypes.viewDOTDiagram.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("View DOT Diagram", () => {
                                    renderDotString(code.trim());
                                })
                            }
                            {fileTypes.runPythonScript.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("Run Python Script", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'Run Python Script',
                                        content: code.trim()
                                    });
                                })
                            }
                            {fileTypes.launchSTL.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("Launch STL", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'Launch STL',
                                        content: code.trim()
                                    });
                                })
                            }
                            {fileTypes.runPowerShellScript.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("Run PowerShell Script", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'Run PowerShell Script',
                                        content: code.trim()
                                    });
                                })
                            }
                            {fileTypes.selectFindAndReplaceScript2.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("Apply", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'ApplyFaRArray',
                                        content: code.trim()
                                    });
                                })
                            }
                            {fileTypes.selectFindAndReplaceScript.includes(fileType.trim().toLowerCase()) &&
                                addMessageButton("Select Find-And-Replace Script", () => {
                                    try {
                                        const fixedJsonString = fixNewlinesInStrings(code.trim());
                                        const parsedJson = JSON.parse(fixedJsonString);
                                        window.currentlySelectedFindAndReplaceSet = parsedJson;
                                        window.selectedMessageGuid = guid;
                                        window.dispatchEvent(new Event('findAndReplaceUpdate'));
                                    } catch (error) {
                                        alert('Error parsing Find-And-Replace script: ' + error);
                                    }
                                })
                            }
                            {currentlySelectedFindAndReplaceSet && selectedMessageGuid &&
                                addMessageButton("Apply Find-And-Replace Script", () => {
                                    window.chrome.webview.postMessage({
                                        type: 'applyFindAndReplace',
                                        content: code.trim(),
                                        guid: guid,
                                        dataType: fileType.trim(),
                                        codeBlockIndex: codeBlockCounter.toString(),
                                        findAndReplaces: JSON.stringify(currentlySelectedFindAndReplaceSet),
                                        selectedMessageGuid: selectedMessageGuid
                                    });
                                    // Reset currentlySelectedFindAndReplaceSet and hide 'Apply...' buttons
                                    window.currentlySelectedFindAndReplaceSet = null;
                                    window.selectedMessageGuid = null;
                                    window.dispatchEvent(new Event('findAndReplaceUpdate'));
                                })
                            }
                        </div>
                    </div>
                    <div style={{
                        fontFamily: 'monospace',
                        whiteSpace: 'pre-wrap',
                        backgroundColor: colorScheme.codeBlockBackgroundColor,
                        color: colorScheme.codeBlockTextColor,
                        padding: '10px',
                        //borderBottomLeftRadius: '5px',
                        //borderBottomRightRadius: '5px',
                        marginBottom: '10px'
                    }}>
                        {code.trim()}
                    </div>
                </div>
            );
            lastIndex = offset + match.length;
            onCodeBlockRendered(); // Increment the counter after rendering a code block
        });

        // Process any remaining text after the last code block
        if (lastIndex < text.length) {
            const remainingText = text.slice(lastIndex);
            parts.push(...formatUrls(remainingText));
        }

        return parts;
    };

    // New helper function to format URLs in non-code text
    const formatUrls = (text) => {
        const urlRegex = /(?:https?|ftp):\/\/[^\s/$.?#].[^\s]*/g;
        const parts = [];
        const textParts = text.split(urlRegex);
        const urlMatches = text.match(urlRegex) || [];

        textParts.forEach((part, index) => {
            if (part) {
                parts.push(<span key={`text-${index}`}>{part}</span>);
            }
            if (index < urlMatches.length) {
                parts.push(
                    <span
                        key={`url-${index}`}
                        onClick={() => {
                            window.chrome.webview.postMessage({
                                type: 'openUrl',
                                content: urlMatches[index]
                            });
                        }}
                        style={{
                            color: colorScheme.linkColor,
                            cursor: 'pointer',
                            textDecoration: 'underline'
                        }}
                    >
                        {urlMatches[index]}
                    </span>
                );
            }
        });

        return parts;
    };

    return <>{formatContent(content)}</>;
};