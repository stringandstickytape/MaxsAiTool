﻿const UserInputBar = () => {

    const { colorScheme } = React.useColorScheme();

    const [sendDisabled, setSendDisabled] = React.useState(false);
    const [cancelDisabled, setCancelDisabled] = React.useState(false);
    const [newDisabled, setNewDisabled] = React.useState(false);
    const [inputContent, setInputContent] = React.useState('');
    const [sendAlternateLabel, setSendAlternateLabel] = React.useState('');
    const [sendAlternateColor, setSendAlternateColor] = React.useState('');
    const [cancelAlternateLabel, setCancelAlternateLabel] = React.useState('');
    const [cancelAlternateColor, setCancelAlternateColor] = React.useState('');
    const [newAlternateLabel, setNewAlternateLabel] = React.useState('');
    const [newAlternateColor, setNewAlternateColor] = React.useState('');
    const [sendButtonLabel, setSendButtonLabel] = React.useState('Send');

    window.setSendButtonLabel = setSendButtonLabel;

    function getTrueIndices(obj) {
        return Object.keys(obj)
            .filter(key => obj[key] === true)
            .join(',');
    }

    const handleSend = () => {

        var toolsEnabled = window.splitButtonState_Tools.itemStates.join(',');
        var toolsEnabledIndices = getTrueIndices(window.splitButtonState_Tools.itemStates);
        
        const selectedTools = window.getSelectedTools ? window.getSelectedTools() : "";
        window.chrome.webview.postMessage({
            type: window.event.shiftKey ? 'sendSecondary' : 'send',
            content: inputContent,
            selectedTools: toolsEnabledIndices
        });
    };

    const handleSendSecondary = () => {

        var toolsEnabled = window.splitButtonState_Tools.itemStates.join(',');
        var toolsEnabledIndices = getTrueIndices(window.splitButtonState_Tools.itemStates);

        const selectedTools = window.getSelectedTools ? window.getSelectedTools() : "";
        window.chrome.webview.postMessage({
            type: 'sendSecondary',
            content: inputContent,
            selectedTools: toolsEnabledIndices
        });
    };

    const handleNew = () => {
        window.chrome.webview.postMessage({ type: 'new' });
        setInputContent("");
    };

    const handleNewWithContext = () => {
        window.chrome.webview.postMessage({ type: 'newWithContext' });
    };

    const handleNewWithPrompt = () => {
        window.chrome.webview.postMessage({ type: 'newWithPrompt' });
    };

    const setSendButtonAlternate = (label, color) => {
        setSendAlternateLabel(label);
        setSendAlternateColor(color);
    };

    const setCancelButtonAlternate = (label, color) => {
        setCancelAlternateLabel(label);
        setCancelAlternateColor(color);
    };

    const setNewButtonAlternate = (label, color) => {
        setNewAlternateLabel(label);
        setNewAlternateColor(color);
    };
    const handleCancel = () => {
        window.chrome.webview.postMessage({ type: 'cancel'});
    };

    const handleInputChange = (newContent) => {
        setInputContent(newContent);
        window.setMessageText("temp-user-msg", newContent);
    };

    const setUserPrompt = (string) => {
        
        setInputContent(string);
        window.setMessageText("temp-user-msg", string);
    };
    window.setUserPrompt = setUserPrompt;

    const getUserPrompt = () => {
        return inputContent;
    };
    window.getUserPrompt = getUserPrompt;

    const disableSendButton = () => setSendDisabled(true);
    const enableSendButton = () => setSendDisabled(false);
    const disableCancelButton = () => setCancelDisabled(true);
    const enableCancelButton = () => setCancelDisabled(false);
    const disableNewButton = () => setNewDisabled(true);
    const enableNewButton = () => setNewDisabled(false);

    // Expose these methods to the window object for external access
    React.useEffect(() => {
        window.disableSendButton = disableSendButton;
        window.enableSendButton = enableSendButton;
        window.disableCancelButton = disableCancelButton;
        window.enableCancelButton = enableCancelButton;
        window.disableNewButton = disableNewButton;
        window.enableNewButton = enableNewButton;
        window.setSendButtonAlternate = setSendButtonAlternate;
        window.setCancelButtonAlternate = setCancelButtonAlternate;
        window.setNewButtonAlternate = setNewButtonAlternate;

        return () => {
            delete window.disableSendButton;
            delete window.enableSendButton;
            delete window.disableCancelButton;
            delete window.enableCancelButton;
            delete window.disableNewButton;
            delete window.enableNewButton;
            delete window.setSendButtonAlternate;
            delete window.setCancelButtonAlternate;
            delete window.setNewButtonAlternate;
        };
    }, []);

    return (
        <>
            <style>
                {`
                    .user-input-bar {
                        display: flex;
                        align-items: stretch;
                        background-color: ${colorScheme.backgroundColor};
                        color: ${colorScheme.textColor};
                        border-top: 1px solid ${colorScheme.headerBackgroundColor};
                    }
                    .input-box-wrapper {
                        flex-grow: 1;
                        margin-right: 10px;
                    }
                    .buttons-wrapper {
                        display: flex;
                        flex-direction: column;
                        width: 100px;
                    }
                    .input-button {
                        margin-bottom: 5px;
                        padding: 8px;
                        border: none;
                        border-radius: 4px;
                        color: ${colorScheme.buttonTextColor};
                        cursor: pointer;
                        font-size: 14px;
                        transition: opacity 0.3s ease;
                    }
                    .input-button:disabled {
                        opacity: 0.5;
                        cursor: not-allowed;
                    }
                    .send-button { background-color: ${colorScheme.buttonBackgroundColor}; background: ${colorScheme.buttonBackgroundCss}; }
                    .cancel-button { background-color: ${colorScheme.buttonBackgroundColor}; background: ${colorScheme.buttonBackgroundCss}; }
                    .new-button { background-color: ${colorScheme.buttonBackgroundColor}; background: ${colorScheme.buttonBackgroundCss}; }

                    .split-button-container {
                        display: flex;
                        margin-bottom: 5px;
                    }
                    .split-button-main, .split-button-arrow {
                        background-color: ${colorScheme.buttonBackgroundColor};
                        background: ${colorScheme.buttonBackgroundCss};
                        color: ${colorScheme.buttonTextColor};
                        border: none;
                        padding: 8px;
                        cursor: pointer;
                        transition: opacity 0.3s ease;
                    }
                    .split-button-main:disabled, .split-button-arrow:disabled {
                        opacity: 0.5;
                        cursor: not-allowed;
                    }
                    .split-button-main {
                        flex-grow: 1;
                        border-radius: 4px 0 0 4px;
                    }
                    .split-button-arrow {
                        border-radius: 0 4px 4px 0;
                    }
                    .split-button-dropdown {
                        position: absolute;
                        bottom: 20px;
                        right: 16px;
                        background-color: ${colorScheme.dropdownBackgroundColor};
                        min-width: 160px;
                        box-shadow: 0px 8px 16px 0px rgba(0,0,0,0.5);
                        z-index: 1;
                    }
                    .split-button-dropdown-item {
                        color: ${colorScheme.dropdownTextColor};
                        padding: 12px 16px;
                        text-decoration: none;
                        display: block;
                        border: none;
                        width: 100%;
                        text-align: left;
                        background-color: transparent;
                        cursor: pointer;
                    }
                    .split-button-dropdown-item:hover {
                        background-color: ${colorScheme.selectedItemBackgroundColor};
                    }
                `}
            </style>
            <div className="user-input-bar">
                <div className="input-box-wrapper">
                    <InputBox
                        onSend={handleSend}
                        value={inputContent}
                        onChange={handleInputChange}
                        placeholderText="Enter prompt..."
                        disabled={sendDisabled}
                        
                    />
                </div>
                <div className="buttons-wrapper">
                    <SplitButton
                        label={sendButtonLabel}
                        onClick={handleSend}
                        disabled={sendDisabled}
                        color={colorScheme.buttonBackgroundColor}
                        background={colorScheme.buttonBackgroundCss}
                        alternateLabel={sendAlternateLabel}
                        alternateColor={sendAlternateColor}
                        dropdownItems={[
                            { label: "Send via Secondary AI", onClick: handleSendSecondary }
                        ]}
                        title="CTRL+Enter to send, CTRL+SHIFT+Enter to send via Secondary AI"
                    />
                    <SplitButton
                        label="Cancel"
                        onClick={handleCancel}
                        disabled={cancelDisabled}
                        color={colorScheme.buttonBackgroundColor}
                        background={colorScheme.buttonBackgroundCss}
                        alternateLabel={cancelAlternateLabel}
                        alternateColor={cancelAlternateColor}
                    />
                    <SplitButton
                        label="New"
                        onClick={handleNew}
                        dropdownItems={[
                            { label: "New with context", onClick: handleNewWithContext },
                            { label: "New with prompt", onClick: handleNewWithPrompt }
                        ]}
                        disabled={newDisabled}
                        color={colorScheme.buttonBackgroundColor}
                        background={colorScheme.buttonBackgroundCss}
                        alternateLabel={newAlternateLabel}
                        alternateColor={newAlternateColor}
                    />
                </div>
            </div>
        </>
    );
};