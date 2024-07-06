﻿function loadScript(url) {
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = url;
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
    });
}

let svg, g, zoom, root;

// Create SVG container
const svgContainer = document.createElement('div');
svgContainer.id = 'svg-container';
svgContainer.style.width = '100%';
svgContainer.style.height = '100%';
document.body.appendChild(svgContainer);

loadScript('https://d3js.org/d3.v7.min.js')
    .then(() => {
        svg = d3.select('#svg-container')
            .append('svg')
            .attr('width', '100%')
            .attr('height', '100%');

        // Initialize the graph
        initializeGraph();
    })
    .catch(error => {
        console.error('Error loading scripts:', error);
    });

function initializeGraph() {
    zoom = d3.zoom()
        .on('zoom', (event) => {
            g.attr('transform', event.transform);
        });

    svg.call(zoom);

    // Add a group for all graph elements
    g = svg.append('g')
        .attr('transform', `translate(${svg.node().clientWidth / 2},${svg.node().clientHeight / 2})`);

    // Initialize root of the tree
    root = { id: 'root', children: [] };
}

function clear() {
    console.log("Clearing graph");
    root.children = [];
    updateGraph();
    console.log("Cleared");
}

function fitAll() {
    if (!root || !root.children.length) return;

    const bounds = g.node().getBBox();
    const fullWidth = svg.node().clientWidth;
    const fullHeight = svg.node().clientHeight;
    const width = bounds.width;
    const height = bounds.height;
    const midX = bounds.x + width / 2;
    const midY = bounds.y + height / 2;

    if (width === 0 || height === 0) return; // nothing to fit

    const scale = 0.95 / Math.max(width / fullWidth, height / fullHeight);
    const translate = [fullWidth / 2 - scale * midX, fullHeight / 2 - scale * midY];

    svg.transition().duration(750)
        .call(
            zoom.transform,
            d3.zoomIdentity
                .translate(translate[0], translate[1])
                .scale(scale)
        );
}

function addNodes(nodes) {
    console.log("Adding nodes: ", nodes);
    nodes.forEach(node => {
        root.children.push({ id: node.id, label: node.label, children: [] });
    });
    updateGraph();
}

function addLinks(links) {
    console.log("Adding links: ", links);
    links.forEach(link => {
        const sourceNode = findNode(root, link.source);
        const targetNode = findNode(root, link.target);
        if (sourceNode && targetNode) {
            if (!sourceNode.children) sourceNode.children = [];
            sourceNode.children.push(targetNode);
            // Remove targetNode from root's direct children
            root.children = root.children.filter(n => n.id !== targetNode.id);
        }
    });
    updateGraph();
}

function findNode(node, id) {
    if (node.id === id) return node;
    if (node.children) {
        for (let child of node.children) {
            const found = findNode(child, id);
            if (found) return found;
        }
    }
    return null;
}

function updateGraph() {
    const nodeWidth = 200;
    const nodeHeight = 40;
    const horizontalSpacing = 100;
    const verticalSpacing = 60;

    // Recursive function to position nodes
    function positionNode(node, x, y, level) {
        node.x = x;
        node.y = y;

        if (node.children && node.children.length > 0) {
            const childrenHeight = (node.children.length - 1) * (nodeHeight + verticalSpacing);
            let childY = y - childrenHeight / 2;

            node.children.forEach(child => {
                positionNode(child, x + nodeWidth + horizontalSpacing, childY, level + 1);
                childY += nodeHeight + verticalSpacing;
            });
        }
    }

    // Position all nodes starting from root
    positionNode(root, 0, 0, 0);

    // Update links
    const links = g.selectAll('.link')
        .data(getLinks(root), d => `${d.source.id}-${d.target.id}`);

    links.enter()
        .append('path')
        .attr('class', 'link')
        .merge(links)
        .attr('fill', 'none')
        .attr('stroke', '#999')
        .attr('d', d => `M${d.source.x + nodeWidth},${d.source.y + nodeHeight / 2} C${(d.source.x + d.target.x + nodeWidth) / 2},${d.source.y + nodeHeight / 2} ${(d.source.x + d.target.x + nodeWidth) / 2},${d.target.y + nodeHeight / 2} ${d.target.x},${d.target.y + nodeHeight / 2}`);

    links.exit().remove();

    // Update nodes
    const nodes = g.selectAll('.node')
        .data(getNodes(root), d => d.id);

    const nodeEnter = nodes.enter()
        .append('g')
        .attr('class', 'node')
        .attr('id', d => d.id);

    nodeEnter.append('rect')
        .attr('width', nodeWidth)
        .attr('height', nodeHeight)
        .attr('fill', '#f2f2f2')
        .attr('stroke', '#666')
        .attr('stroke-width', 1);

    nodeEnter.append('text')
        .attr('dy', '0.31em')
        .attr('x', 5)
        .attr('y', nodeHeight / 2)
        .attr('text-anchor', 'start')
        .attr('fill', 'black')
        .attr('font-size', '12px')
        .text(d => d.label.length > 25 ? d.label.substring(0, 25) + '...' : d.label);

    nodes.merge(nodeEnter)
        .transition()
        .duration(750)
        .attr('transform', d => `translate(${d.x},${d.y})`);

    nodes.exit().remove();

    fitAll();
}

function getNodes(node) {
    let nodes = [node];
    if (node.children) {
        node.children.forEach(child => {
            nodes = nodes.concat(getNodes(child));
        });
    }
    return nodes;
}

function getLinks(node) {
    let links = [];
    if (node.children) {
        node.children.forEach(child => {
            links.push({ source: node, target: child });
            links = links.concat(getLinks(child));
        });
    }
    return links;
}

function centerOnNode(id) {
    console.log("Centre on node");
    const node = g.select(`#${id}`);
    if (node.empty()) {
        console.warn(`Node with id '${id}' not found.`);
        return;
    }

    const transform = d3.zoomTransform(svg.node());
    const bounds = node.node().getBBox();
    const fullWidth = svg.node().clientWidth;
    const fullHeight = svg.node().clientHeight;
    const scale = 0.5; // You can adjust this value to change the zoom level
    const x = bounds.x + bounds.width / 2;
    const y = bounds.y + bounds.height / 2;

    svg.transition().duration(750)
        .call(
            zoom.transform,
            d3.zoomIdentity
                .translate(fullWidth / 2, fullHeight / 2)
                .scale(scale)
                .translate(-x, -y)
        );
}