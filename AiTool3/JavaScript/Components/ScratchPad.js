﻿const { useState, useEffect, useCallback, useRef } = React;

const ScratchPad = () => {
    console.log("ScratchPad component rendering");

    const scratchPadWindowRef = useRef(null);
    const pillButtonRef = useRef(null);
    const [currentSelection, setCurrentSelection] = useState('');
    const selectionInProgressRef = useRef(false);
    const mousePositionRef = useRef({ x: 0, y: 0 });

    const createPillButton = useCallback(() => {
        console.log("Creating pill button");
        if (pillButtonRef.current && document.body.contains(pillButtonRef.current)) return;

        const button = document.createElement('button');
        button.id = 'scratchPadPillButton';
        button.textContent = 'Copy to Scratch Pad';
        button.style.position = 'fixed';
        button.style.zIndex = '1000';
        button.style.borderRadius = '20px';
        button.style.display = 'none';
        button.style.padding = '5px 10px';
        button.style.backgroundColor = '#007bff';
        button.style.color = 'white';
        button.style.border = 'none';
        button.style.cursor = 'pointer';
        button.style.left = '50%';
        button.style.top = '50%';
        button.style.transform = 'translate(-50%, -50%)';
        document.body.appendChild(button);
        pillButtonRef.current = button;
        console.log("Pill button created and added to the DOM");
    }, []);

    const showPillButton = useCallback((x, y) => {
        console.log("Showing pill button");
        const button = pillButtonRef.current;
        if (!button) {
            console.log("Pill button not found");
            return;
        }

        const buttonRect = button.getBoundingClientRect();
        const windowWidth = window.innerWidth;
        const windowHeight = window.innerHeight;

        let left = x + 10;
        let top = y + 10;

        if (left + buttonRect.width > windowWidth) {
            left = windowWidth - buttonRect.width - 10;
        }
        if (top + buttonRect.height > windowHeight) {
            top = windowHeight - buttonRect.height - 10;
        }

        button.style.left = `${left}px`;
        button.style.top = `${top}px`;
        button.style.display = 'block';
        button.style.transform = 'none';
        console.log("Pill button displayed at", left, top);
    }, []);

    const hidePillButton = useCallback(() => {
        console.log("Hiding pill button");
        if (pillButtonRef.current) {
            pillButtonRef.current.style.display = 'none';
        }
    }, []);

    const copyToScratchPad = useCallback(() => {
        console.log("Copying to scratch pad");
        if (!scratchPadWindowRef.current || scratchPadWindowRef.current.closed) {
            createScratchPadWindow();
        }
        scratchPadWindowRef.current.document.getElementById('scratchPadContent').value += currentSelection + '\n\n';
        hidePillButton();
        clearSelection();
    }, [currentSelection, hidePillButton]);

    const createScratchPadWindow = useCallback(() => {
        console.log("Creating scratch pad window");
        const newWindow = window.open('', 'ScratchPad', 'width=400,height=400');
        newWindow.document.write(`
            <html>
            <head>
                <title>Scratch Pad</title>
                <style>
                    body { font-family: Arial, sans-serif; margin: 0; padding: 10px; }
                    #scratchPadContent { width: 100%; height: 90vh; resize: none; }
                </style>
            </head>
            <body>
                <textarea id="scratchPadContent"></textarea>
            </body>
            </html>
        `);
        scratchPadWindowRef.current = newWindow;
    }, []);

    const clearSelection = useCallback(() => {
        console.log("Clearing selection");
        if (window.getSelection) {
            window.getSelection().removeAllRanges();
        } else if (document.selection) {
            document.selection.empty();
        }
    }, []);

    const checkSelection = useCallback((x, y) => {
        console.log("Checking selection");
        const selection = window.getSelection();
        const newSelection = selection.toString().trim();
        console.log("New selection:", newSelection);
        setCurrentSelection(newSelection);
        if (newSelection.length > 0) {
            console.log("Selection not empty, showing pill button at", x, y);
            showPillButton(x, y);
        } else {
            console.log("Selection empty, hiding pill button");
            hidePillButton();
        }
    }, [showPillButton, hidePillButton]);

    useEffect(() => {
        console.log("Setting up ScratchPad effect");
        createPillButton();

        const handleSelectionEnd = (e) => {
            console.log("Selection ended");
            if (selectionInProgressRef.current) {
                selectionInProgressRef.current = false;
                setTimeout(() => checkSelection(e.clientX, e.clientY), 10);
            }
        };

        const handleKeyDown = (e) => {
            if (e.shiftKey) {
                console.log("Shift key pressed");
                selectionInProgressRef.current = true;
            }
        };

        const handleKeyUp = (e) => {
            if (!e.shiftKey && selectionInProgressRef.current) {
                console.log("Shift key released");
                selectionInProgressRef.current = false;
                setTimeout(() => checkSelection(mousePositionRef.current.x, mousePositionRef.current.y), 10);
            }
        };

        const handleDocumentClick = (e) => {
            if (pillButtonRef.current && !pillButtonRef.current.contains(e.target)) {
                console.log("Document clicked, hiding pill button");
                hidePillButton();
            }
        };

        const updateMousePosition = (e) => {
            mousePositionRef.current = { x: e.clientX, y: e.clientY };
        };

        document.addEventListener('mousedown', () => {
            console.log("Mouse down, selection in progress");
            selectionInProgressRef.current = true;
        });
        document.addEventListener('mouseup', handleSelectionEnd);
        document.addEventListener('keydown', handleKeyDown);
        document.addEventListener('keyup', handleKeyUp);
        document.addEventListener('click', handleDocumentClick);
        document.addEventListener('mousemove', updateMousePosition);

        if (pillButtonRef.current) {
            pillButtonRef.current.addEventListener('click', copyToScratchPad);
        }

        return () => {
            console.log("Cleaning up ScratchPad effect");
            document.removeEventListener('mousedown', () => { selectionInProgressRef.current = true; });
            document.removeEventListener('mouseup', handleSelectionEnd);
            document.removeEventListener('keydown', handleKeyDown);
            document.removeEventListener('keyup', handleKeyUp);
            document.removeEventListener('click', handleDocumentClick);
            document.removeEventListener('mousemove', updateMousePosition);
            if (pillButtonRef.current) {
                pillButtonRef.current.removeEventListener('click', copyToScratchPad);
            }
            // Note: We're not removing the pill button from the DOM on cleanup
        };
    }, [createPillButton, checkSelection, hidePillButton, copyToScratchPad]);

    return null;
};

console.log("export");

window.ScratchPad = ScratchPad;