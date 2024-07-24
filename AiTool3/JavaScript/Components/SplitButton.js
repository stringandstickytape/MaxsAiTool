﻿const { useState, useEffect, useRef } = React;

const SplitButton = ({ label, onClick, dropdownItems = [], disabled, color = '#007bff' }) => {
    const [isOpen, setIsOpen] = useState(false);
    const dropdownRef = useRef(null);
    const hasSplit = dropdownItems.length > 0;
    const uniqueId = useRef(`split-button-${Math.random().toString(36).substr(2, 9)}`).current;

    useEffect(() => {
        const handleClickOutside = (event) => {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target)) {
                setIsOpen(false);
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, []);

    const buttonStyle = {
        backgroundColor: color,
        color: 'white',
        border: 'none',
        padding: '8px 8px',
        cursor: 'pointer',
        transition: 'background-color 0.3s',


    };

    const mainButtonStyle = {
        ...buttonStyle,
        flexGrow: 1,
        textAlign: 'center', 
        borderRadius: hasSplit ? '4px 0 0 4px' : '4px',
    };

    const arrowButtonStyle = {
        ...buttonStyle,
        borderRadius: '0 4px 4px 0',
        padding: '8px 8px',
        borderLeft: '3px solid #333',
    };

    const dropdownStyle = {
        position: 'absolute',
        left: '-44px',
        bottom: '15px',
        backgroundColor: 'white',
        border: '1px solid #ccc',
        borderRadius: '4px',
        boxShadow: '0 2px 5px rgba(0,0,0,0.1)',
        zIndex: 1000,
    };

    const dropdownItemStyle = {
        display: 'block',
        width: '100%',
        padding: '10px 15px',
        textAlign: 'left',
        background: 'none',
        border: 'none',
        cursor: 'pointer',
    };

    return (
        <div style={{ display: 'inline-flex', position: 'relative', padding: '4px' }} id={uniqueId}>
            <button
                style={mainButtonStyle}
                onClick={onClick}
                disabled={disabled}
                onMouseEnter={(e) => e.target.style.backgroundColor = adjustColor(color, -20)}
                onMouseLeave={(e) => e.target.style.backgroundColor = color}
            >
                {label}
            </button>
            {hasSplit && (
                <>
                    <button
                        style={arrowButtonStyle}
                        onClick={() => setIsOpen(!isOpen)}
                        disabled={disabled}
                        onMouseEnter={(e) => e.target.style.backgroundColor = adjustColor(color, -20)}
                        onMouseLeave={(e) => e.target.style.backgroundColor = color}
                    >
                        ▼
                    </button>
                    {isOpen && (
                        <div style={dropdownStyle} ref={dropdownRef}>
                            {dropdownItems.map((item, index) => (
                                <button
                                    key={index}
                                    style={dropdownItemStyle}
                                    onClick={() => {
                                        item.onClick();
                                        setIsOpen(false);
                                    }}
                                    onMouseEnter={(e) => e.target.style.backgroundColor = '#f0f0f0'}
                                    onMouseLeave={(e) => e.target.style.backgroundColor = 'white'}
                                >
                                    {item.label}
                                </button>
                            ))}
                        </div>
                    )}
                </>
            )}
        </div>
    );
};

// Helper function to adjust color brightness
function adjustColor(color, amount) {
    return '#' + color.replace(/^#/, '').replace(/../g, color => ('0' + Math.min(255, Math.max(0, parseInt(color, 16) + amount)).toString(16)).substr(-2));
}